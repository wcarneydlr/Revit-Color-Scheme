using Autodesk.Revit.DB;
using ColorSchemeAddin.Models;
using ColorSchemeAddin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace ColorSchemeAddin.ViewModels
{
    public partial class CreateSchemeViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly MainDashboardViewModel _main;

        // ── Method selection ───────────────────────────────────────────────
        [ObservableProperty] private bool _isUploadMethod  = true;
        [ObservableProperty] private bool _isScratchMethod;

        // ── Upload Excel ───────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<ColorSchemeModel> _importedSchemes = new();
        [ObservableProperty] private ColorSchemeModel? _selectedImportedScheme;
        [ObservableProperty] private string _importFilePath = string.Empty;

        // ── Create from scratch ────────────────────────────────────────────
        [ObservableProperty] private ColorSchemeModel _scratchScheme = new();
        [ObservableProperty] private string _scratchSchemeName = string.Empty;

        // ── Create Options panel ───────────────────────────────────────────
        [ObservableProperty] private bool _showCreateOptions;

        // Fill parameter
        [ObservableProperty] private ObservableCollection<string> _availableParameters = new();
        [ObservableProperty] private string _parameterName = "Department";

        // New parameter creation
        [ObservableProperty] private bool   _showParamInput;
        [ObservableProperty] private string _newParameterName = string.Empty;

        // Categories
        [ObservableProperty] private bool _applyRooms         = true;
        [ObservableProperty] private bool _applyAreas;
        [ObservableProperty] private bool _applyMasses;
        [ObservableProperty] private bool _applyGenericModels;
        [ObservableProperty] private bool _applyFloors;

        // Area scheme picker
        [ObservableProperty] private ObservableCollection<AreaScheme> _availableAreaSchemes = new();
        [ObservableProperty] private AreaScheme? _selectedAreaScheme;
        [ObservableProperty] private bool _createNewAreaScheme;

        [ObservableProperty] private string _lastResult = string.Empty;

        public CreateSchemeViewModel(Document doc, MainDashboardViewModel main)
        {
            _doc  = doc;
            _main = main;
            ResetScratchScheme();
            LoadDefaultParameters();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    try { LoadAreaSchemes(); }    catch { }
                    try { RefreshParameters(); } catch { }
                }));
        }

        // ── Show options ───────────────────────────────────────────────────

        [RelayCommand]
        private void ShowUploadOptions()
        {
            if (SelectedImportedScheme == null && string.IsNullOrEmpty(ImportFilePath))
            {
                MessageBox.Show("Please browse and select an Excel file first.",
                    "No File Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShowCreateOptions = true;
        }

        [RelayCommand]
        private void ShowScratchOptions()
        {
            if (string.IsNullOrWhiteSpace(ScratchSchemeName))
            {
                MessageBox.Show("Please enter a scheme name first.",
                    "Name Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (ScratchScheme.Entries.Count == 0)
            {
                MessageBox.Show("Please add at least one color entry first.",
                    "No Entries", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShowCreateOptions = true;
        }

        [RelayCommand]
        private void HideOptions()
        {
            ShowCreateOptions     = false;
            ShowParamInput = false;
        }

        // ── Parameter creation ─────────────────────────────────────────────

        [RelayCommand]
        private void RevealNewParameterInput() => ShowParamInput = true;

        [RelayCommand]
        private void CancelNewParameter()
        {
            ShowParamInput = false;
            NewParameterName      = string.Empty;
        }

        [RelayCommand]
        private void CreateNewParameter()
        {
            if (string.IsNullOrWhiteSpace(NewParameterName))
            {
                MessageBox.Show("Please enter a parameter name.",
                    "Name Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var categories = GetSelectedCategories();
                if (!categories.Any())
                {
                    MessageBox.Show(
                        "Please select at least one category in 'Applies to' " +
                        "before creating a parameter — the parameter will be " +
                        "bound to those categories.",
                        "No Categories", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var svc  = new ParameterService(_doc);
                string created = svc.CreateProjectParameter(
                    NewParameterName.Trim(), categories);

                // Add to dropdown and select it
                if (!AvailableParameters.Contains(created))
                    AvailableParameters.Add(created);
                ParameterName = created;

                ShowParamInput = false;
                NewParameterName      = string.Empty;
                _main.SetStatus($"Created parameter '{created}' and added it to the list.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Parameter Creation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Final create ───────────────────────────────────────────────────

        [RelayCommand]
        private void CreateScheme()
        {
            if (IsUploadMethod)
                ExecuteCreateFromImport();
            else
                ExecuteCreateFromScratch();
        }

        // ── Browse / download ──────────────────────────────────────────────

        [RelayCommand]
        private void BrowseImportFile()
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select Color Scheme Excel File",
                Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                ImportFilePath = dlg.FileName;
                LoadFromExcel(dlg.FileName);
            }
        }

        [RelayCommand]
        private void DownloadTemplate()
        {
            var dlg = new SaveFileDialog
            {
                Title    = "Save Color Scheme Template",
                Filter   = "Excel Files (*.xlsx)|*.xlsx",
                FileName = "DLR_Color_Scheme_Template.xlsx"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    ExcelService.ExportTemplate(dlg.FileName);
                    LastResult = $"Template saved to {Path.GetFileName(dlg.FileName)}";
                    _main.SetStatus(LastResult);
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        // ── Scratch entry management ───────────────────────────────────────

        [RelayCommand]
        private void AddScratchEntry()
        {
            ScratchScheme.Entries.Add(new ColorEntryModel
            {
                Value = "New Entry", ColorName = "New Entry",
                R = 128, G = 128, B = 128
            });
        }

        [RelayCommand]
        private void RemoveScratchEntry(ColorEntryModel? entry)
        {
            if (entry != null) ScratchScheme.Entries.Remove(entry);
        }

        // ── Private execution ──────────────────────────────────────────────

        private void ExecuteCreateFromImport()
        {
            if (SelectedImportedScheme == null)
            {
                MessageBox.Show("Please import an Excel file and select a scheme.",
                    "No Scheme Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SelectedImportedScheme.ParameterName = ParameterName;
            TryCreateOrUpdate(SelectedImportedScheme);
        }

        private void ExecuteCreateFromScratch()
        {
            ScratchScheme.Name          = ScratchSchemeName;
            ScratchScheme.ParameterName = ParameterName;
            TryCreateOrUpdate(ScratchScheme);
            ResetScratchScheme();
        }

        private void TryCreateOrUpdate(ColorSchemeModel model)
        {
            try
            {
                _main.SetStatus($"Creating '{model.Name}'…", true);
                var service    = new ColorFillSchemeService(_doc);
                var categories = GetSelectedCategories().ToList();

                var primaryCat = categories.FirstOrDefault();
                var catId = primaryCat != default
                    ? new ElementId(primaryCat)
                    : new ElementId(BuiltInCategory.OST_Rooms);

                ColorFillScheme? targetScheme;
                var existing = service.FindByName(model.Name);
                if (existing != null)
                {
                    service.UpdateScheme(existing, model);
                    targetScheme = existing;
                    LastResult = $"Updated '{model.Name}' — {model.Entries.Count} entries.";
                }
                else
                {
                    service.CreateScheme(model, catId);
                    // Re-fetch after creation so we have the live element
                    targetScheme = service.FindByName(model.Name);
                    LastResult = $"Created '{model.Name}' — {model.Entries.Count} entries.";
                }

                // ── Persist applied categories + parameter to extensible storage ──
                if (targetScheme != null && categories.Any())
                {
                    SchemeMetadataService.SaveMetadata(
                        _doc, targetScheme, categories, ParameterName);
                }

                if (ApplyAreas)
                    EnsureAreaScheme(model);

                ShowCreateOptions = false;
                _main.SetStatus(LastResult);
                _main.NavigateToManage();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void EnsureAreaScheme(ColorSchemeModel model)
        {
            try
            {
                var areaSvc = new AreaSchemeService(_doc);
                var target  = CreateNewAreaScheme || SelectedAreaScheme == null
                    ? areaSvc.GetOrCreateColorSchemesAreaScheme()
                    : SelectedAreaScheme;

                // Create/update the ColorFillScheme within this area scheme
                // so it appears in the area plan color fill dialog with values
                var cfService = new ColorFillSchemeService(_doc);
                cfService.EnsureAreaColorFillScheme(model, target);

                _main.SetStatus($"{LastResult} | Area scheme '{target.Name}' ready.");
            }
            catch (Exception ex)
            {
                _main.SetStatus($"Note: Area scheme — {ex.Message}");
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private IEnumerable<BuiltInCategory> GetSelectedCategories()
        {
            var list = new System.Collections.Generic.List<BuiltInCategory>();
            if (ApplyRooms)         list.Add(BuiltInCategory.OST_Rooms);
            if (ApplyAreas)         list.Add(BuiltInCategory.OST_Areas);
            if (ApplyMasses)        list.Add(BuiltInCategory.OST_Mass);
            if (ApplyGenericModels) list.Add(BuiltInCategory.OST_GenericModel);
            if (ApplyFloors)        list.Add(BuiltInCategory.OST_Floors);
            return list;
        }

        private void LoadDefaultParameters()
        {
            AvailableParameters.Clear();
            foreach (var p in new[]
            {
                "Department", "Name", "Occupancy", "Space Type",
                "Phase", "Level", "Function", "Comments"
            })
                AvailableParameters.Add(p);
            ParameterName = "Department";
        }

        private void RefreshParameters()
        {
            try
            {
                var svc = new ParameterService(_doc);
                var cats = new[]
                {
                    BuiltInCategory.OST_Rooms, BuiltInCategory.OST_Areas,
                    BuiltInCategory.OST_Floors, BuiltInCategory.OST_Mass,
                    BuiltInCategory.OST_GenericModel
                };
                var current = ParameterName;
                AvailableParameters.Clear();
                foreach (var p in svc.GetTextParameterNames(cats))
                    AvailableParameters.Add(p);
                // Preserve selection if it still exists
                ParameterName = AvailableParameters.Contains(current)
                    ? current : (AvailableParameters.FirstOrDefault() ?? "Department");
            }
            catch { }
        }

        private void LoadFromExcel(string path)
        {
            try
            {
                _main.SetStatus("Reading Excel file…", true);
                var schemes = ExcelService.ImportFromFile(path);
                ImportedSchemes.Clear();
                foreach (var s in schemes) ImportedSchemes.Add(s);
                SelectedImportedScheme = ImportedSchemes.FirstOrDefault();
                LastResult = $"Loaded {schemes.Count} scheme(s) from {Path.GetFileName(path)}.";
                _main.SetStatus(LastResult);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void LoadAreaSchemes()
        {
            try
            {
                AvailableAreaSchemes.Clear();
                var svc = new AreaSchemeService(_doc);
                foreach (var s in svc.GetAllAreaSchemes())
                    AvailableAreaSchemes.Add(s);
                SelectedAreaScheme = AvailableAreaSchemes.FirstOrDefault();
            }
            catch { }
        }

        private void ResetScratchScheme()
        {
            ScratchScheme     = new ColorSchemeModel();
            ScratchSchemeName = string.Empty;
        }

        private void ShowError(Exception ex)
        {
            LastResult = $"Error: {ex.Message}";
            _main.SetStatus("Operation failed");
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
