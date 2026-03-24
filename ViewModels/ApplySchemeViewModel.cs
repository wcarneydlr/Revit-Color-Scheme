using Autodesk.Revit.DB;
using ColorSchemeAddin.Models;
using ColorSchemeAddin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace ColorSchemeAddin.ViewModels
{
    public enum ApplyMethod
    {
        CreateMaterials,
        ApplyToCurrentView,
        ApplyToSelectedViews,
        ApplyToViewTemplates,
        GenerateViewTemplates
    }

    public partial class ApplySchemeViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly MainDashboardViewModel _main;

        // ── Step 1 ─────────────────────────────────────────────────────────
        [ObservableProperty] private ApplyMethod _selectedMethod = ApplyMethod.ApplyToCurrentView;
        [ObservableProperty] private bool _isOnStep1 = true;

        // ── Step 2 ─────────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<ColorFillScheme> _availableSchemes = new();
        [ObservableProperty] private ColorFillScheme? _selectedScheme;

        // Active view options
        [ObservableProperty] private bool _applyTemporary = true;
        [ObservableProperty] private bool _applyPermanent;

        // Selected views
        [ObservableProperty] private bool _removeExistingTemplates;
        [ObservableProperty] private bool _updateExistingTemplates;
        [ObservableProperty] private ObservableCollection<ViewSelectionItem> _selectableViews = new();

        // View templates for "Apply to view templates" option
        [ObservableProperty] private ObservableCollection<ViewSelectionItem> _selectableTemplates = new();

        // Generate view templates options
        [ObservableProperty] private bool _vtFloorPlan = true;
        [ObservableProperty] private bool _vtAreaPlan;
        [ObservableProperty] private bool _vtThreeD;
        [ObservableProperty] private bool _vtSection;
        [ObservableProperty] private bool _vtUseColorFill = true;
        [ObservableProperty] private bool _vtUseFilters;

        [ObservableProperty] private string _lastResult = string.Empty;

        public ApplySchemeViewModel(Document doc, MainDashboardViewModel main)
        {
            _doc  = doc;
            _main = main;
        }

        // ── Step 1 ─────────────────────────────────────────────────────────

        [RelayCommand]
        private void Next()
        {
            RefreshSchemes();
            LoadSelectableViews();
            LoadSelectableTemplates();
            IsOnStep1 = false;
        }

        [RelayCommand]
        private void Back() => IsOnStep1 = true;

        // ── Step 2 ─────────────────────────────────────────────────────────

        [RelayCommand]
        public void RefreshSchemes()
        {
            AvailableSchemes.Clear();
            var service = new ColorFillSchemeService(_doc);
            foreach (var s in service.GetAllSchemes())
                AvailableSchemes.Add(s);
            SelectedScheme = AvailableSchemes.FirstOrDefault();
        }

        [RelayCommand]
        private void ApplyScheme()
        {
            if (SelectedScheme == null)
            {
                MessageBox.Show("Please select a color scheme.",
                    "No Scheme", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _main.SetStatus("Applying…", true);

                var cfService = new ColorFillSchemeService(_doc);
                var vtService = new ViewTemplateService(_doc);
                var model     = cfService.ToModel(SelectedScheme);

                switch (SelectedMethod)
                {
                    case ApplyMethod.CreateMaterials:
                        ExecuteCreateMaterials(model);
                        break;

                    case ApplyMethod.ApplyToCurrentView:
                        ExecuteApplyToCurrentView(model, vtService);
                        break;

                    case ApplyMethod.ApplyToSelectedViews:
                        ExecuteApplyToSelectedViews(model, vtService);
                        break;

                    case ApplyMethod.ApplyToViewTemplates:
                        ExecuteApplyToViewTemplates(model, vtService);
                        break;

                    case ApplyMethod.GenerateViewTemplates:
                        ExecuteGenerateViewTemplates(model, vtService);
                        break;
                }

                _main.SetStatus(LastResult);

                // Save metadata so scan picks up the applied categories
                SchemeMetadataService.SaveMetadata(
                    _doc, SelectedScheme,
                    GetModelCategories(model),
                    model.ParameterName ?? "Department");
            }
            catch (Exception ex)
            {
                LastResult = $"Error: {ex.Message}";
                _main.SetStatus("Apply failed");
                MessageBox.Show(ex.Message, "Apply Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Apply implementations ──────────────────────────────────────────

        private void ExecuteCreateMaterials(ColorSchemeModel model)
        {
            var matSvc = new MaterialService(_doc);
            var mats   = matSvc.SyncMaterials(model);
            LastResult = $"Created/updated {mats.Count} material(s) for '{SelectedScheme!.Name}'.";
        }

        private void ExecuteApplyToCurrentView(ColorSchemeModel model,
            ViewTemplateService vtService)
        {
            var activeView = _doc.ActiveView;
            if (activeView == null)
            {
                MessageBox.Show("No active view.", "No Active View",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ApplySchemeToView manages its own transactions internally
            vtService.ApplySchemeToView(activeView, model, SelectedScheme);

            LastResult = $"Applied '{SelectedScheme!.Name}' to '{activeView.Name}'.";
        }

        private void ExecuteApplyToSelectedViews(ColorSchemeModel model,
            ViewTemplateService vtService)
        {
            var selected = SelectableViews.Where(v => v.IsSelected).ToList();
            if (!selected.Any())
            {
                MessageBox.Show("Select at least one view.",
                    "No Views Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var views = selected
                .Select(item => _doc.GetElement(item.ViewId) as View)
                .Where(v => v != null)
                .Cast<View>()
                .ToList();

            // ApplySchemeToViews manages its own transactions internally
            vtService.ApplySchemeToViews(views, model, SelectedScheme);

            LastResult = $"Applied '{SelectedScheme!.Name}' to {selected.Count} view(s).";
        }

        private void ExecuteApplyToViewTemplates(ColorSchemeModel model,
            ViewTemplateService vtService)
        {
            // Use selected templates if any are checked, else apply to all
            var selectedIds = SelectableTemplates
                .Where(t => t.IsSelected)
                .Select(t => t.ViewId)
                .ToHashSet();

            List<View> templates;
            if (selectedIds.Any())
            {
                templates = selectedIds
                    .Select(id => _doc.GetElement(id) as View)
                    .Where(v => v != null)
                    .Cast<View>()
                    .ToList();
            }
            else
            {
                templates = new FilteredElementCollector(_doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.IsTemplate)
                    .ToList();
            }

            if (!templates.Any())
            {
                MessageBox.Show("No view templates found. Select at least one template " +
                    "or ensure templates exist in the document.",
                    "None Found", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Use ApplySchemeToViews which handles its own transactions correctly
            vtService.ApplySchemeToViews(templates, model, SelectedScheme);
            LastResult = $"Applied '{SelectedScheme!.Name}' to {templates.Count} template(s).";
        }

        private void ExecuteGenerateViewTemplates(ColorSchemeModel model,
            ViewTemplateService vtService)
        {
            var viewTypes = new List<ViewTemplateType>();
            if (VtFloorPlan) viewTypes.Add(ViewTemplateType.FloorPlan);
            if (VtAreaPlan)  viewTypes.Add(ViewTemplateType.AreaPlan);
            if (VtThreeD)    viewTypes.Add(ViewTemplateType.ThreeD);
            if (VtSection)   viewTypes.Add(ViewTemplateType.Section);

            if (!viewTypes.Any())
            {
                MessageBox.Show("Select at least one view type.",
                    "No View Types", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Check for missing source views before starting
            var missing = new List<string>();
            foreach (var vt in viewTypes)
            {
                if (!vtService.HasSourceViewOfType(vt))
                    missing.Add(vtService.ViewTypeName(vt));
            }

            if (missing.Any())
            {
                // 3D views can be auto-created; plan views need user action
                var planMissing = missing.Where(m => m != "3D").ToList();
                var needs3D     = missing.Contains("3D");

                if (planMissing.Any())
                {
                    string msg = $"No source view found for: {string.Join(", ", planMissing)}.\n\n" +
                                 "Please create at least one view of each missing type in Revit, " +
                                 "then try again.\n\nWould you like to continue with the available view types only?";

                    var result = MessageBox.Show(msg, "Missing Source Views",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No) return;

                    // Remove the types we can't handle
                    foreach (var m in planMissing)
                        viewTypes.RemoveAll(vt => vtService.ViewTypeName(vt) == m);
                }

                if (needs3D && !planMissing.Any())
                {
                    var result = MessageBox.Show(
                        "No 3D view found in the document.\n\n" +
                        "Would you like to create a default isometric 3D view to use as " +
                        "the source for the template?",
                        "Create 3D View?",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.No)
                        viewTypes.Remove(ViewTemplateType.ThreeD);
                    // If Yes, CreateViewTemplateOfType will create it automatically
                }
            }

            if (!viewTypes.Any())
            {
                MessageBox.Show("No view types available to process.",
                    "Nothing to Do", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var method = (VtUseColorFill, VtUseFilters) switch
            {
                (true, true)  => ColorApplicationMethod.Both,
                (false, true) => ColorApplicationMethod.ViewFilters,
                _             => ColorApplicationMethod.ColorFillScheme
            };

            var schemeMap = new Dictionary<string, ColorFillScheme>
            {
                { model.Name, SelectedScheme! }
            };

            var created = vtService.BatchCreateTemplates(
                new[] { model }, viewTypes, method, schemeMap);

            LastResult = $"Created {created.Count} view template(s) for '{model.Name}'.";
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private void LoadSelectableTemplates()
        {
            SelectableTemplates.Clear();
            var templates = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .OrderBy(v => v.Name);

            foreach (var v in templates)
                SelectableTemplates.Add(new ViewSelectionItem
                {
                    ViewId   = v.Id,
                    ViewName = v.Name,
                    ViewType = v.ViewType.ToString()
                });
        }

        private void LoadSelectableViews()
        {
            SelectableViews.Clear();
            var views = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate &&
                            v.ViewType != ViewType.Schedule &&
                            v.ViewType != ViewType.DrawingSheet)
                .OrderBy(v => v.ViewType.ToString())
                .ThenBy(v => v.Name);

            foreach (var v in views)
                SelectableViews.Add(new ViewSelectionItem
                {
                    ViewId   = v.Id,
                    ViewName = v.Name,
                    ViewType = v.ViewType.ToString()
                });
        }

        private static IEnumerable<BuiltInCategory> GetModelCategories(ColorSchemeModel model)
        {
            var list = new List<BuiltInCategory>();
            if (model.ApplyToRooms)         list.Add(BuiltInCategory.OST_Rooms);
            if (model.ApplyToAreas)         list.Add(BuiltInCategory.OST_Areas);
            if (model.ApplyToFloors)        list.Add(BuiltInCategory.OST_Floors);
            if (model.ApplyToGenericModels) list.Add(BuiltInCategory.OST_GenericModel);
            if (model.ApplyToMasses)        list.Add(BuiltInCategory.OST_Mass);
            return list;
        }
    }

    public partial class ViewSelectionItem : ObservableObject
    {
        [ObservableProperty] private bool _isSelected;
        public ElementId ViewId   { get; set; } = ElementId.InvalidElementId;
        public string ViewName    { get; set; } = string.Empty;
        public string ViewType    { get; set; } = string.Empty;
    }
}
