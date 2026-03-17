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
    public partial class ApplySchemeViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly MainDashboardViewModel _main;

        // ── Scheme selection ───────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<ColorFillScheme> _availableSchemes = new();
        [ObservableProperty] private ColorFillScheme? _selectedScheme;

        // ── Application options ────────────────────────────────────────────
        [ObservableProperty] private bool _applyToFloorPlans = true;
        [ObservableProperty] private bool _applyToAreaPlans;
        [ObservableProperty] private bool _createMaterials;
        [ObservableProperty] private bool _createViewFilters;
        [ObservableProperty] private bool _useTemporaryViewSetting;

        // ── View template generation ───────────────────────────────────────
        [ObservableProperty] private bool _generateTemplates;
        [ObservableProperty] private bool _vtFloorPlan = true;
        [ObservableProperty] private bool _vtAreaPlan;
        [ObservableProperty] private bool _vtThreeD;
        [ObservableProperty] private bool _vtSection;
        [ObservableProperty] private bool _vtUseColorFill = true;
        [ObservableProperty] private bool _vtUseFilters;

        // ── Multi-scheme template batch ────────────────────────────────────
        [ObservableProperty] private ObservableCollection<ApplySchemeRowModel> _batchSchemeRows = new();

        [ObservableProperty] private string _lastResult = string.Empty;

        public ApplySchemeViewModel(Document doc, MainDashboardViewModel main)
        {
            _doc = doc;
            _main = main;
            RefreshSchemes();
        }

        // ── Commands ───────────────────────────────────────────────────────

        [RelayCommand]
        public void RefreshSchemes()
        {
            try
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
            catch (Exception ex)
            {
                _main.SetStatus($"Error refreshing schemes: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ApplyColorFillScheme()
        {
            if (SelectedScheme == null) { NoSchemeMessage(); return; }
            try
            {
                var service = new ColorFillSchemeService(_doc);
                if (ApplyToFloorPlans) service.ApplySchemeToFloorPlans(SelectedScheme);
                if (ApplyToAreaPlans)  service.ApplySchemeToAreaPlans(SelectedScheme);
                LastResult = $"Applied '{SelectedScheme.Name}' to views.";
                _main.SetStatus(LastResult);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        [RelayCommand]
        private void CreateMaterialsCommand()
        {
            if (SelectedScheme == null) { NoSchemeMessage(); return; }
            try
            {
                _main.SetStatus("Creating materials…", true);
                var cfService = new ColorFillSchemeService(_doc);
                var model = cfService.ToModel(SelectedScheme);
                var matService = new MaterialService(_doc);
                var results = matService.SyncMaterials(model);
                LastResult = $"Created/updated {results.Count} material(s) for '{SelectedScheme.Name}'.";
                _main.SetStatus(LastResult);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        [RelayCommand]
        private void CreateViewFiltersCommand()
        {
            if (SelectedScheme == null) { NoSchemeMessage(); return; }
            try
            {
                _main.SetStatus("Creating view filters…", true);
                var cfService = new ColorFillSchemeService(_doc);
                var model = cfService.ToModel(SelectedScheme);
                var vtService = new ViewTemplateService(_doc);

                using var tx = new Transaction(_doc, "Create Color Scheme View Filters");
                tx.Start();
                var filters = vtService.GetOrCreateFiltersForScheme(model);
                tx.Commit();

                LastResult = $"Created/updated {filters.Count} filter(s) for '{SelectedScheme.Name}'.";
                _main.SetStatus(LastResult);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        [RelayCommand]
        private void ApplyTemporaryView()
        {
            if (SelectedScheme == null) { NoSchemeMessage(); return; }
            try
            {
                var activeView = _doc.ActiveView;
                if (activeView == null)
                {
                    MessageBox.Show("No active view in the document.", "No Active View",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var cfService = new ColorFillSchemeService(_doc);
                var model = cfService.ToModel(SelectedScheme);
                var vtService = new ViewTemplateService(_doc);

                using var tx = new Transaction(_doc, "Apply Temporary Color Scheme View");
                tx.Start();
                var filters = vtService.GetOrCreateFiltersForScheme(model);
                vtService.ApplyFiltersToView(activeView, model, filters);
                tx.Commit();

                LastResult = $"Applied temporary view settings to '{activeView.Name}'.";
                _main.SetStatus(LastResult);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        [RelayCommand]
        private void GenerateViewTemplates()
        {
            var selectedSchemes = BatchSchemeRows.Where(r => r.IsSelected).ToList();
            if (!selectedSchemes.Any())
            {
                MessageBox.Show("Select at least one scheme from the batch list.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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
                (true, true)   => ColorApplicationMethod.Both,
                (true, false)  => ColorApplicationMethod.ColorFillScheme,
                (false, true)  => ColorApplicationMethod.ViewFilters,
                _ => ColorApplicationMethod.ColorFillScheme
            };

            try
            {
                _main.SetStatus("Generating view templates…", true);
                var cfService = new ColorFillSchemeService(_doc);
                var vtService = new ViewTemplateService(_doc);

                var models = selectedSchemes.Select(r => cfService.ToModel(r.Scheme)).ToList();
                var schemeMap = selectedSchemes.ToDictionary(r => r.Name, r => r.Scheme);

                var created = vtService.BatchCreateTemplates(models, viewTypes, method, schemeMap);
                LastResult = $"Created {created.Count} view template(s).";
                _main.SetStatus(LastResult);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private void NoSchemeMessage() =>
            MessageBox.Show("Please select a color scheme first.", "No Scheme Selected",
                MessageBoxButton.OK, MessageBoxImage.Information);

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
}
