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
using System.Windows;

namespace ColorSchemeAddin.ViewModels
{
    public partial class ManageSchemeViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly MainDashboardViewModel _main;

        [ObservableProperty] private ObservableCollection<ColorSchemeRowModel> _schemeRows = new();
        [ObservableProperty] private ColorSchemeRowModel? _selectedRow;

        public ManageSchemeViewModel(Document doc, MainDashboardViewModel main)
        {
            _doc  = doc;
            _main = main;
        }

        // ── Commands ───────────────────────────────────────────────────────

        [RelayCommand]
        public void RefreshSchemes()
        {
            try
            {
                SchemeRows.Clear();
                var service = new ColorFillSchemeService(_doc);
                var matSvc  = new MaterialService(_doc);
                var vtSvc   = new ViewTemplateService(_doc);

                foreach (var scheme in service.GetAllSchemes())
                {
                    var model = service.ToModel(scheme);
                    var mats  = matSvc.GetMaterialsForScheme(scheme.Name);

                    // Detect which application methods are active for this scheme
                    var row = new ColorSchemeRowModel
                    {
                        RevitScheme      = scheme,
                        Name             = scheme.Name,
                        EntryCount       = model.Entries.Count,
                        CategoryName     = GetCategoryName(scheme),
                        Model            = model,
                        HasMaterials     = mats.Count > 0,
                        HasRooms         = scheme.CategoryId == new ElementId(BuiltInCategory.OST_Rooms),
                        HasAreas         = scheme.CategoryId == new ElementId(BuiltInCategory.OST_Areas),
                        HasFloors        = scheme.CategoryId == new ElementId(BuiltInCategory.OST_Floors),
                        HasGenericModels = scheme.CategoryId == new ElementId(BuiltInCategory.OST_GenericModel),
                    };

                    // Detect view templates by naming convention
                    var allTemplates = new FilteredElementCollector(_doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => v.Name.StartsWith(scheme.Name + " - "))
                        .ToList();

                    row.Has3D        = allTemplates.Any(v => v.Name.EndsWith("3D"));
                    row.HasFloorPlan = allTemplates.Any(v => v.Name.EndsWith("Floor Plan"));
                    row.HasAreaPlan  = allTemplates.Any(v => v.Name.EndsWith("Area Plan"));

                    // Status: applied if any view uses this scheme
                    row.IsApplied = IsSchemeAppliedToAnyView(scheme);

                    SchemeRows.Add(row);
                }

                _main.SetStatus($"Loaded {SchemeRows.Count} color scheme(s).");
            }
            catch (Exception ex)
            {
                _main.SetStatus($"Error loading schemes: {ex.Message}");
            }
        }

        [RelayCommand]
        private void EditScheme(ColorSchemeRowModel? row)
        {
            row ??= SelectedRow;
            if (row == null) return;

            var editor = new SchemeEditorDialog(row.Model, _doc);
            if (editor.ShowDialog() == true)
            {
                try
                {
                    var service = new ColorFillSchemeService(_doc);
                    service.UpdateScheme(row.RevitScheme, row.Model);
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

        [RelayCommand]
        private void SelectAll(bool selected)
        {
            foreach (var row in SchemeRows) row.IsSelected = selected;
        }

        // ── Helpers ────────────────────────────────────────────────────────

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

        private bool IsSchemeAppliedToAnyView(ColorFillScheme scheme)
        {
            try
            {
                return new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .Any(v =>
                    {
                        var p = v.LookupParameter("Color Scheme");
                        return p != null && p.AsElementId() == scheme.Id;
                    });
            }
            catch { return false; }
        }
    }

    public partial class ColorSchemeRowModel : ObservableObject
    {
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private int  _entryCount;
        [ObservableProperty] private bool _isApplied;

        public ColorFillScheme  RevitScheme      { get; set; } = null!;
        public string           Name             { get; set; } = string.Empty;
        public string           CategoryName     { get; set; } = string.Empty;
        public ColorSchemeModel Model            { get; set; } = null!;

        // Application method flags — shown as columns in the grid
        public bool HasGenericModels { get; set; }
        public bool HasRooms         { get; set; }
        public bool HasAreas         { get; set; }
        public bool HasFloors        { get; set; }
        public bool HasMaterials     { get; set; }
        public bool Has3D            { get; set; }
        public bool HasFloorPlan     { get; set; }
        public bool HasAreaPlan      { get; set; }
    }
}
