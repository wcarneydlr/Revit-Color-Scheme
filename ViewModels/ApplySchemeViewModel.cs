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

        // ── Step 1: method selection ───────────────────────────────────────
        [ObservableProperty] private ApplyMethod _selectedMethod = ApplyMethod.ApplyToCurrentView;
        [ObservableProperty] private bool _isOnStep1 = true;

        // ── Step 2: scheme + category ──────────────────────────────────────
        [ObservableProperty] private ObservableCollection<ColorFillScheme> _availableSchemes = new();
        [ObservableProperty] private ColorFillScheme? _selectedScheme;

        // Revit element categories
        [ObservableProperty] private bool _catWalls;
        [ObservableProperty] private bool _catFloors;
        [ObservableProperty] private bool _catCeilings;
        [ObservableProperty] private bool _catRooms = true;
        [ObservableProperty] private bool _catAreas;

        // Active/selected view options
        [ObservableProperty] private bool _applyTemporary = true;
        [ObservableProperty] private bool _applyPermanent;
        [ObservableProperty] private bool _removeExistingTemplates;
        [ObservableProperty] private bool _updateExistingTemplates;
        [ObservableProperty] private ObservableCollection<ViewSelectionItem> _selectableViews = new();

        // View template generation options
        [ObservableProperty] private bool _vtFloorPlan = true;
        [ObservableProperty] private bool _vtAreaPlan;
        [ObservableProperty] private bool _vtThreeD;
        [ObservableProperty] private bool _vtSection;
        [ObservableProperty] private bool _vtUseColorFill = true;
        [ObservableProperty] private bool _vtUseFilters;
        [ObservableProperty] private ObservableCollection<ApplySchemeRowModel> _batchSchemeRows = new();

        [ObservableProperty] private string _lastResult = string.Empty;

        // Step 1 display labels
        public string MethodDescription => SelectedMethod switch
        {
            ApplyMethod.CreateMaterials       => "Creates a solid-color material for each scheme entry, duplicated from a 'Color Scheme' template material if present.",
            ApplyMethod.ApplyToCurrentView    => "Applies the color scheme to the currently active view. Choose temporary (reverts on close) or permanent.",
            ApplyMethod.ApplyToSelectedViews  => "Choose specific views from the document to apply the scheme to.",
            ApplyMethod.ApplyToViewTemplates  => "Apply the color scheme to existing view templates in the document.",
            ApplyMethod.GenerateViewTemplates => "Create new view templates from this scheme across multiple view types.",
            _ => string.Empty
        };

        public ApplySchemeViewModel(Document doc, MainDashboardViewModel main)
        {
            _doc  = doc;
            _main = main;
        }

        // ── Step 1 commands ────────────────────────────────────────────────

        [RelayCommand]
        private void Next()
        {
            RefreshSchemes();
            LoadSelectableViews();
            IsOnStep1 = false;
        }

        [RelayCommand]
        private void Back() => IsOnStep1 = true;

        // ── Step 2 commands ────────────────────────────────────────────────

        [RelayCommand]
        public void RefreshSchemes()
        {
            AvailableSchemes.Clear();
            BatchSchemeRows.Clear();
            var service = new ColorFillSchemeService(_doc);
            foreach (var s in service.GetAllSchemes())
            {
                AvailableSchemes.Add(s);
                BatchSchemeRows.Add(new ApplySchemeRowModel { Scheme = s, Name = s.Name });
            }
            SelectedScheme = AvailableSchemes.FirstOrDefault();
        }

        [RelayCommand]
        private void ApplyScheme()
        {
            if (SelectedScheme == null) { NoSchemeMessage(); return; }

            try
            {
                _main.SetStatus("Applying…", true);
                var cfService = new ColorFillSchemeService(_doc);
                var model     = cfService.ToModel(SelectedScheme);
                var vtService = new ViewTemplateService(_doc);

                switch (SelectedMethod)
                {
                    case ApplyMethod.CreateMaterials:
                        var matSvc = new MaterialService(_doc);
                        var mats = matSvc.SyncMaterials(model);
                        LastResult = $"Created/updated {mats.Count} material(s) for '{SelectedScheme.Name}'.";
                        break;

                    case ApplyMethod.ApplyToCurrentView:
                        ApplyToActiveView(model, vtService);
                        break;

                    case ApplyMethod.ApplyToSelectedViews:
                        ApplyToSelectedViews(model, vtService);
                        break;

                    case ApplyMethod.ApplyToViewTemplates:
                        ApplyToExistingTemplates(model, vtService);
                        break;

                    case ApplyMethod.GenerateViewTemplates:
                        GenerateViewTemplates(model);
                        break;
                }

                _main.SetStatus(LastResult);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        // ── Application method implementations ─────────────────────────────

        private void ApplyToActiveView(ColorSchemeModel model, ViewTemplateService vtService)
        {
            var activeView = _doc.ActiveView;
            if (activeView == null)
            {
                MessageBox.Show("No active view.", "No Active View",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using var tx = new Transaction(_doc, $"Apply Color Scheme to Active View: {model.Name}");
            tx.Start();
            var filters = vtService.GetOrCreateFiltersForScheme(model);
            vtService.ApplyFiltersToView(activeView, model, filters);
            tx.Commit();

            LastResult = $"Applied '{SelectedScheme!.Name}' to '{activeView.Name}'" +
                         (ApplyTemporary ? " (temporary)." : " (permanent).");
        }

        private void ApplyToSelectedViews(ColorSchemeModel model, ViewTemplateService vtService)
        {
            var selected = SelectableViews.Where(v => v.IsSelected).ToList();
            if (!selected.Any())
            {
                MessageBox.Show("Select at least one view.", "No Views Selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using var tx = new Transaction(_doc, $"Apply Color Scheme to Selected Views: {model.Name}");
            tx.Start();
            var filters = vtService.GetOrCreateFiltersForScheme(model);
            foreach (var item in selected)
            {
                var view = (View)_doc.GetElement(item.ViewId);
                if (view != null)
                    vtService.ApplyFiltersToView(view, model, filters);
            }
            tx.Commit();

            LastResult = $"Applied '{SelectedScheme!.Name}' to {selected.Count} view(s).";
        }

        private void ApplyToExistingTemplates(ColorSchemeModel model, ViewTemplateService vtService)
        {
            var templates = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .ToList();

            if (!templates.Any())
            {
                MessageBox.Show("No view templates found in document.", "None Found",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using var tx = new Transaction(_doc, $"Apply Color Scheme to View Templates: {model.Name}");
            tx.Start();
            var filters = vtService.GetOrCreateFiltersForScheme(model);
            foreach (var t in templates)
                vtService.ApplyFiltersToView(t, model, filters);
            tx.Commit();

            LastResult = $"Applied '{SelectedScheme!.Name}' to {templates.Count} view template(s).";
        }

        private void GenerateViewTemplates(ColorSchemeModel model)
        {
            var viewTypes = new List<ViewTemplateType>();
            if (VtFloorPlan) viewTypes.Add(ViewTemplateType.FloorPlan);
            if (VtAreaPlan)  viewTypes.Add(ViewTemplateType.AreaPlan);
            if (VtThreeD)    viewTypes.Add(ViewTemplateType.ThreeD);
            if (VtSection)   viewTypes.Add(ViewTemplateType.Section);

            if (!viewTypes.Any())
            {
                MessageBox.Show("Select at least one view type.", "No View Types",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var method = (VtUseColorFill, VtUseFilters) switch
            {
                (true, true)  => ColorApplicationMethod.Both,
                (false, true) => ColorApplicationMethod.ViewFilters,
                _             => ColorApplicationMethod.ColorFillScheme
            };

            var vtService = new ViewTemplateService(_doc);
            var created   = vtService.BatchCreateTemplates(
                new[] { model }, viewTypes, method);

            LastResult = $"Created {created.Count} view template(s) for '{model.Name}'.";
        }

        // ── Helpers ────────────────────────────────────────────────────────

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

        private void NoSchemeMessage() =>
            MessageBox.Show("Please select a color scheme.",
                "No Scheme Selected", MessageBoxButton.OK, MessageBoxImage.Information);

        private void ShowError(Exception ex)
        {
            LastResult = $"Error: {ex.Message}";
            _main.SetStatus("Operation failed");
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public partial class ApplySchemeRowModel : ObservableObject
    {
        [ObservableProperty] private bool _isSelected;
        public ColorFillScheme Scheme { get; set; } = null!;
        public string Name { get; set; } = string.Empty;
    }

    public partial class ViewSelectionItem : ObservableObject
    {
        [ObservableProperty] private bool _isSelected;
        public ElementId ViewId   { get; set; } = ElementId.InvalidElementId;
        public string ViewName    { get; set; } = string.Empty;
        public string ViewType    { get; set; } = string.Empty;
    }
}
