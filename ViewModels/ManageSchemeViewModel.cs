using Autodesk.Revit.DB;
using ColorSchemeAddin.Models;
using ColorSchemeAddin.Services;
using ColorSchemeAddin.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ColorSchemeAddin.ViewModels
{
    public partial class ManageSchemeViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly MainDashboardViewModel _main;

        [ObservableProperty] private ObservableCollection<ColorSchemeRowModel> _schemeRows = new();
        [ObservableProperty] private ColorSchemeRowModel? _selectedRow;
        [ObservableProperty] private bool _isScanning;

        public ManageSchemeViewModel(Document doc, MainDashboardViewModel main)
        {
            _doc  = doc;
            _main = main;
        }

        [RelayCommand]
        public void RefreshSchemes()
        {
            try
            {
                SchemeRows.Clear();
                var service = new ColorFillSchemeService(_doc);

                foreach (var scheme in service.GetAllSchemes())
                {
                    var model = service.ToModel(scheme);
                    // Load stored categories from extensible storage
                    var (storedCats, _) = SchemeMetadataService.LoadMetadata(scheme);
                    bool usesStorage = storedCats.Count > 0;

                    var row   = new ColorSchemeRowModel
                    {
                        RevitScheme      = scheme,
                        Name             = scheme.Name,
                        EntryCount       = model.Entries.Count,
                        CategoryName     = GetCategoryName(scheme),
                        Model            = model,
                        // Use stored categories if available, else fall back to primary CategoryId
                        HasRooms         = usesStorage
                            ? storedCats.Contains(BuiltInCategory.OST_Rooms)
                            : scheme.CategoryId == new ElementId(BuiltInCategory.OST_Rooms),
                        HasAreas         = usesStorage
                            ? storedCats.Contains(BuiltInCategory.OST_Areas)
                            : scheme.CategoryId == new ElementId(BuiltInCategory.OST_Areas),
                        HasFloors        = usesStorage
                            ? storedCats.Contains(BuiltInCategory.OST_Floors)
                            : scheme.CategoryId == new ElementId(BuiltInCategory.OST_Floors),
                        HasGenericModels = usesStorage
                            ? storedCats.Contains(BuiltInCategory.OST_GenericModel)
                            : scheme.CategoryId == new ElementId(BuiltInCategory.OST_GenericModel),
                        ScanStatus       = ScanStatus.Pending,
                    };

                    // Template-based detection by naming convention
                    var templates = new FilteredElementCollector(_doc)
                        .OfClass(typeof(View)).Cast<View>()
                        .Where(v => v.Name.StartsWith(scheme.Name + " - ")).ToList();
                    row.Has3D        = templates.Any(v => v.Name.EndsWith("3D"));
                    row.HasFloorPlan = templates.Any(v => v.Name.EndsWith("Floor Plan"));
                    row.HasAreaPlan  = templates.Any(v => v.Name.EndsWith("Area Plan"));

                    SchemeRows.Add(row);
                }

                _main.SetStatus($"Loaded {SchemeRows.Count} scheme(s). Scanning views…");
                ScanAppliedStatusAsync();
            }
            catch (Exception ex)
            {
                _main.SetStatus($"Error loading schemes: {ex.Message}");
            }
        }

        // ── Applied-status scan ────────────────────────────────────────────

        private void ScanAppliedStatusAsync()
        {
            IsScanning = true;

            var schemeIdToRow = SchemeRows.ToDictionary(r => r.RevitScheme.Id);
            var allSchemeIds  = new HashSet<ElementId>(schemeIdToRow.Keys);

            // ── Collect all data from Revit API on UI thread ───────────────
            // 1. Color fill schemes applied directly to views (Rooms/Areas)
            var viewHits = new List<(ViewType viewType, HashSet<ElementId> schemeIds)>();

            // 2. View template names created by this addin: "{schemeName} - *"
            //    Map: templateId -> set of scheme row names that match
            var templateToSchemeNames = new Dictionary<ElementId, HashSet<string>>();

            // 3. Filter names created by this addin: "{schemeName} - {entryValue}"
            //    Map: filterId -> scheme name
            var filterToSchemeName = new Dictionary<ElementId, string>();

            // 4. Material names created by this addin: "{schemeName} - {entryValue}"
            //    Map: materialId -> scheme name
            var materialToSchemeName = new Dictionary<ElementId, string>();

            try
            {
                var schemeNames = SchemeRows.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // ── Color fill: Rooms / Areas via GetColorFillSchemeId ─────
                var allViews = new FilteredElementCollector(_doc)
                    .OfClass(typeof(View)).Cast<View>().ToList();

                var templateColorSchemeIds = new Dictionary<ElementId, HashSet<ElementId>>();

                foreach (var v in allViews.Where(v => v.IsTemplate))
                {
                    var ids = GetColorFillSchemeIds(v, allSchemeIds);
                    if (ids.Count > 0) templateColorSchemeIds[v.Id] = ids;
                }

                foreach (var v in allViews.Where(v => !v.IsTemplate))
                {
                    if (v.ViewType != ViewType.FloorPlan &&
                        v.ViewType != ViewType.CeilingPlan &&
                        v.ViewType != ViewType.AreaPlan &&
                        v.ViewType != ViewType.ThreeD) continue;

                    var found = GetColorFillSchemeIds(v, allSchemeIds);
                    if (v.ViewTemplateId != ElementId.InvalidElementId &&
                        templateColorSchemeIds.TryGetValue(v.ViewTemplateId, out var tIds))
                        found.UnionWith(tIds);

                    if (found.Count > 0) viewHits.Add((v.ViewType, found));
                }

                // ── View templates created by addin ────────────────────────
                // Template naming: "{schemeName} - Floor Plan" / "- 3D" / "- Area Plan"
                foreach (var v in allViews.Where(v => v.IsTemplate))
                {
                    foreach (var schemeName in schemeNames)
                    {
                        if (v.Name.StartsWith(schemeName + " - ",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            if (!templateToSchemeNames.ContainsKey(v.Id))
                                templateToSchemeNames[v.Id] = new HashSet<string>(
                                    StringComparer.OrdinalIgnoreCase);
                            templateToSchemeNames[v.Id].Add(schemeName);
                        }
                    }
                }

                // Check which non-template views use these templates
                var viewsUsingSchemeTemplates = new Dictionary<string, HashSet<ViewType>>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var v in allViews.Where(v => !v.IsTemplate &&
                    v.ViewTemplateId != ElementId.InvalidElementId))
                {
                    if (!templateToSchemeNames.TryGetValue(v.ViewTemplateId,
                        out var names)) continue;

                    foreach (var name in names)
                    {
                        if (!viewsUsingSchemeTemplates.ContainsKey(name))
                            viewsUsingSchemeTemplates[name] = new HashSet<ViewType>();
                        viewsUsingSchemeTemplates[name].Add(v.ViewType);
                    }
                }

                // ── Filters created by addin ───────────────────────────────
                // Filter naming: "{schemeName} - {entryValue}"
                var allFilters = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>().ToList();

                foreach (var f in allFilters)
                {
                    foreach (var schemeName in schemeNames)
                    {
                        if (f.Name.StartsWith(schemeName + " - ",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            filterToSchemeName[f.Id] = schemeName;
                            break;
                        }
                    }
                }

                // Check which views have these filters applied
                var viewsUsingSchemeFilters = new Dictionary<string, HashSet<ViewType>>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var v in allViews.Where(v => !v.IsTemplate))
                {
                    try
                    {
                        foreach (var filterId in v.GetFilters())
                        {
                            if (!filterToSchemeName.TryGetValue(filterId,
                                out var schemeName)) continue;
                            if (!viewsUsingSchemeFilters.ContainsKey(schemeName))
                                viewsUsingSchemeFilters[schemeName] = new HashSet<ViewType>();
                            viewsUsingSchemeFilters[schemeName].Add(v.ViewType);
                        }
                    }
                    catch { }
                }

                // ── Materials created by addin ─────────────────────────────
                // Material naming: "{schemeName} - {entryValue}"
                var allMaterials = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>().ToList();

                var schemeMaterialIds = new Dictionary<string, HashSet<ElementId>>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var mat in allMaterials)
                {
                    foreach (var schemeName in schemeNames)
                    {
                        if (mat.Name.StartsWith(schemeName + " - ",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            if (!schemeMaterialIds.ContainsKey(schemeName))
                                schemeMaterialIds[schemeName] = new HashSet<ElementId>();
                            schemeMaterialIds[schemeName].Add(mat.Id);
                            materialToSchemeName[mat.Id] = schemeName;
                            break;
                        }
                    }
                }

                // Check which elements use these materials
                var schemesWithMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (schemeMaterialIds.Count > 0)
                {
                    // Check Generic Models, Masses, Floors for material matches
                    var categoriesToCheck = new[]
                    {
                        BuiltInCategory.OST_GenericModel,
                        BuiltInCategory.OST_Mass,
                        BuiltInCategory.OST_Floors,
                    };

                    foreach (var bic in categoriesToCheck)
                    {
                        try
                        {
                            var elements = new FilteredElementCollector(_doc)
                                .OfCategory(bic)
                                .WhereElementIsNotElementType()
                                .ToList();

                            foreach (var el in elements)
                            {
                                try
                                {
                                    var matIds = el.GetMaterialIds(false);
                                    foreach (var matId in matIds)
                                    {
                                        if (materialToSchemeName.TryGetValue(matId,
                                            out var schemeName))
                                            schemesWithMaterials.Add(schemeName);
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }

                // ── Background matching ────────────────────────────────────
                var uiDispatcher = Dispatcher.CurrentDispatcher;

                // Build final results: color fill hits (by schemeId)
                var colorFillResults = new Dictionary<ElementId, (bool fp, bool ap, bool td)>();
                foreach (var (viewType, foundIds) in viewHits)
                {
                    foreach (var id in foundIds)
                    {
                        colorFillResults.TryGetValue(id, out var cur);
                        colorFillResults[id] = viewType switch
                        {
                            ViewType.FloorPlan or ViewType.CeilingPlan => (true, cur.ap, cur.td),
                            ViewType.AreaPlan  => (cur.fp, true, cur.td),
                            ViewType.ThreeD    => (cur.fp, cur.ap, true),
                            _                  => cur
                        };
                    }
                }

                Task.Run(() =>
                {
                    foreach (var kvp in schemeIdToRow)
                    {
                        var id   = kvp.Key;
                        var row  = kvp.Value;
                        var name = row.Name;

                        colorFillResults.TryGetValue(id, out var cf);

                        // Template-based application
                        viewsUsingSchemeTemplates.TryGetValue(name, out var tmplTypes);
                        bool tmplFp = tmplTypes?.Contains(ViewType.FloorPlan) == true ||
                                      tmplTypes?.Contains(ViewType.CeilingPlan) == true;
                        bool tmplAp = tmplTypes?.Contains(ViewType.AreaPlan) == true;
                        bool tmplTd = tmplTypes?.Contains(ViewType.ThreeD) == true;

                        // Filter-based application
                        viewsUsingSchemeFilters.TryGetValue(name, out var filtTypes);
                        bool filtFp = filtTypes?.Contains(ViewType.FloorPlan) == true;
                        bool filtAp = filtTypes?.Contains(ViewType.AreaPlan) == true;
                        bool filtTd = filtTypes?.Contains(ViewType.ThreeD) == true;

                        // Material-based application
                        bool hasMaterials = schemesWithMaterials.Contains(name);

                        bool fp  = cf.fp || tmplFp || filtFp;
                        bool ap  = cf.ap || tmplAp || filtAp;
                        bool td  = cf.td || tmplTd || filtTd;
                        bool any = fp || ap || td || hasMaterials;

                        uiDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                        {
                            row.IsApplied          = any;
                            row.IsFloorPlanApplied = fp;
                            row.IsAreaPlanApplied  = ap;
                            row.Is3DApplied        = td;
                            row.IsRoomsApplied     = row.HasRooms  && (cf.fp || tmplFp || filtFp);
                            row.IsAreasApplied     = row.HasAreas  && (cf.ap || tmplAp || filtAp);
                            row.HasMaterials       = hasMaterials;
                            row.ScanStatus         = ScanStatus.Done;
                        }));
                    }

                    uiDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        IsScanning = false;
                        int applied = SchemeRows.Count(r => r.IsApplied);
                        _main.SetStatus(
                            $"Loaded {SchemeRows.Count} scheme(s) — {applied} applied.");
                    }));
                });
            }
            catch (Exception ex)
            {
                IsScanning = false;
                _main.SetStatus($"Scan error: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns ColorFillScheme ids applied to a view via View.GetColorFillSchemeId().
        /// </summary>
        private HashSet<ElementId> GetColorFillSchemeIds(View view,
            HashSet<ElementId> knownSchemeIds)
        {
            var found = new HashSet<ElementId>();
            try
            {
                var categoriesToCheck = new[]
                {
                    BuiltInCategory.OST_Rooms,
                    BuiltInCategory.OST_Areas,
                    BuiltInCategory.OST_MEPSpaces,
                };

                foreach (var bic in categoriesToCheck)
                {
                    try
                    {
                        var catId    = new ElementId(bic);
                        var schemeId = view.GetColorFillSchemeId(catId);
                        if (schemeId != ElementId.InvalidElementId &&
                            knownSchemeIds.Contains(schemeId))
                            found.Add(schemeId);
                    }
                    catch { }
                }
            }
            catch { }
            return found;
        }

        // ── Other commands ───────────────────────────────────────────────── ─────────────────────────────────────────────────

        [RelayCommand]
        private void EditScheme(ColorSchemeRowModel? row)
        {
            row ??= SelectedRow;
            if (row == null) return;

            var editor = new SchemeEditorDialog(row.Model, _doc);
            if (editor.ShowDialog() == true && editor.Saved)
            {
                try
                {
                    var service = new ColorFillSchemeService(_doc);
                    service.UpdateScheme(row.RevitScheme, row.Model);

                    // Persist applied categories + parameter to extensible storage
                    var cats = new List<BuiltInCategory>();
                    if (row.Model.ApplyToRooms)         cats.Add(BuiltInCategory.OST_Rooms);
                    if (row.Model.ApplyToAreas)         cats.Add(BuiltInCategory.OST_Areas);
                    if (row.Model.ApplyToFloors)        cats.Add(BuiltInCategory.OST_Floors);
                    if (row.Model.ApplyToGenericModels) cats.Add(BuiltInCategory.OST_GenericModel);
                    if (row.Model.ApplyToMasses)        cats.Add(BuiltInCategory.OST_Mass);

                    SchemeMetadataService.SaveMetadata(
                        _doc, row.RevitScheme, cats, row.Model.ParameterName ?? "Department");

                    _main.SetStatus($"Updated scheme '{row.Name}'.");
                    RefreshSchemes();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Update Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void ExportSelected()
        {
            var toExport = SchemeRows.Where(r => r.IsSelected).Select(r => r.Model).ToList();
            if (!toExport.Any())
            {
                MessageBox.Show("Check at least one scheme to export.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DoExport(toExport);
        }

        [RelayCommand]
        private void ExportAll()
        {
            if (!SchemeRows.Any())
            {
                MessageBox.Show("No schemes to export.",
                    "Empty", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DoExport(SchemeRows.Select(r => r.Model));
        }

        [RelayCommand]
        private void AddNewScheme() => _main.NavigateToCreate();

        private void DoExport(IEnumerable<ColorSchemeModel> models)
        {
            var dlg = new SaveFileDialog
            {
                Title    = "Export Color Schemes to Excel",
                Filter   = "Excel Files (*.xlsx)|*.xlsx",
                FileName = "DLR_Color_Schemes_Export.xlsx"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    ExcelService.ExportToFile(models, dlg.FileName);
                    _main.SetStatus($"Exported to {Path.GetFileName(dlg.FileName)}.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string GetCategoryName(ColorFillScheme scheme)
        {
            try
            {
                var cat = Category.GetCategory(_doc, scheme.CategoryId);
                return cat?.Name ?? "Unknown";
            }
            catch { return "Unknown"; }
        }
    }

    public enum ScanStatus { Pending, Done }

    public partial class ColorSchemeRowModel : ObservableObject
    {
        [ObservableProperty] private bool       _isSelected;
        [ObservableProperty] private int        _entryCount;
        [ObservableProperty] private bool       _isApplied;
        [ObservableProperty] private bool       _isRoomsApplied;
        [ObservableProperty] private bool       _isAreasApplied;
        [ObservableProperty] private bool       _isFloorPlanApplied;
        [ObservableProperty] private bool       _isAreaPlanApplied;
        [ObservableProperty] private bool       _is3DApplied;
        [ObservableProperty] private ScanStatus _scanStatus = ScanStatus.Pending;

        public ColorFillScheme  RevitScheme      { get; set; } = null!;
        public string           Name             { get; set; } = string.Empty;
        public string           CategoryName     { get; set; } = string.Empty;
        public ColorSchemeModel Model            { get; set; } = null!;

        public bool HasGenericModels { get; set; }
        public bool HasRooms         { get; set; }
        public bool HasAreas         { get; set; }
        public bool HasFloors        { get; set; }
        [ObservableProperty] private bool _hasMaterials;
        public bool Has3D            { get; set; }
        public bool HasFloorPlan     { get; set; }
        public bool HasAreaPlan      { get; set; }
    }
}
