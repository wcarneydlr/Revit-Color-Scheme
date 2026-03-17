using Autodesk.Revit.DB;
using ColorSchemeAddin.Models;
using ColorSchemeAddin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace ColorSchemeAddin.ViewModels
{
    public partial class CreateSchemeViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly MainDashboardViewModel _main;

        // ── Bound properties ───────────────────────────────────────────────

        [ObservableProperty] private ObservableCollection<ColorSchemeModel> _importedSchemes = new();
        [ObservableProperty] private ColorSchemeModel? _selectedImportedScheme;
        [ObservableProperty] private ObservableCollection<ColorFillScheme> _existingRevitSchemes = new();
        [ObservableProperty] private ColorFillScheme? _selectedRevitScheme;
        [ObservableProperty] private string _newSchemeName = string.Empty;
        [ObservableProperty] private string _parameterName = "Department";
        [ObservableProperty] private bool _targetRooms = true;
        [ObservableProperty] private bool _targetAreas;
        [ObservableProperty] private string _importFilePath = string.Empty;
        [ObservableProperty] private string _lastResult = string.Empty;

        public static string[] AvailableParameters => new[]
        {
            "Department", "Name", "Occupancy", "Space Type",
            "Phase", "Level", "Function", "Comments"
        };

        public CreateSchemeViewModel(Document doc, MainDashboardViewModel main)
        {
            _doc = doc;
            _main = main;
            LoadExistingRevitSchemes();
        }

        // ── Commands ───────────────────────────────────────────────────────

        [RelayCommand]
        private void BrowseImportFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Color Scheme Excel File",
                Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
                FilterIndex = 1
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
                Title = "Save Color Scheme Template",
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = "DLR_Color_Scheme_Template.xlsx"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    ExcelService.ExportTemplate(dlg.FileName);
                    LastResult = $"Template saved to {Path.GetFileName(dlg.FileName)}";
                    _main.SetStatus($"Template downloaded: {dlg.FileName}");
                }
                catch (Exception ex)
                {
                    LastResult = $"Error: {ex.Message}";
                    MessageBox.Show(ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void CreateFromImport()
        {
            if (SelectedImportedScheme == null)
            {
                MessageBox.Show("Please import an Excel file and select a scheme.", "No Scheme Selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _main.SetStatus($"Creating scheme: {SelectedImportedScheme.Name}…", true);

                var service = new ColorFillSchemeService(_doc);

                // Check for duplicate
                if (service.FindByName(SelectedImportedScheme.Name) != null)
                {
                    var result = MessageBox.Show(
                        $"A scheme named '{SelectedImportedScheme.Name}' already exists.\nUpdate it with the imported colors?",
                        "Scheme Exists", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        var existing = service.FindByName(SelectedImportedScheme.Name)!;
                        service.UpdateScheme(existing, SelectedImportedScheme);
                        LastResult = $"Updated scheme '{SelectedImportedScheme.Name}' with {SelectedImportedScheme.Entries.Count} entries.";
                    }
                    else return;
                }
                else
                {
                    var catId = TargetAreas
                        ? new ElementId(BuiltInCategory.OST_Areas)
                        : new ElementId(BuiltInCategory.OST_Rooms);

                    service.CreateScheme(SelectedImportedScheme, catId);
                    LastResult = $"Created scheme '{SelectedImportedScheme.Name}' with {SelectedImportedScheme.Entries.Count} entries.";
                }

                _main.SetStatus(LastResult);
                _main.NavigateToManage();
            }
            catch (Exception ex)
            {
                LastResult = $"Error: {ex.Message}";
                _main.SetStatus($"Error creating scheme");
                MessageBox.Show(ex.Message, "Create Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void ImportFromRevitScheme()
        {
            if (SelectedRevitScheme == null)
            {
                MessageBox.Show("Please select an existing Revit color scheme.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var service = new ColorFillSchemeService(_doc);
            var model = service.ToModel(SelectedRevitScheme);
            model.ApplyToRooms = TargetRooms;
            model.ApplyToAreas = TargetAreas;
            model.ParameterName = ParameterName;

            // Add to imported list so user can review before creating
            ImportedSchemes.Add(model);
            SelectedImportedScheme = model;
            LastResult = $"Loaded '{model.Name}' from Revit ({model.Entries.Count} entries). Review and click 'Create Scheme'.";
            _main.SetStatus(LastResult);
        }

        // ── Helpers ────────────────────────────────────────────────────────

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
            catch (Exception ex)
            {
                LastResult = $"Import error: {ex.Message}";
                _main.SetStatus("Import failed");
                MessageBox.Show(ex.Message, "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadExistingRevitSchemes()
        {
            try
            {
                ExistingRevitSchemes.Clear();
                var service = new ColorFillSchemeService(_doc);
                foreach (var s in service.GetAllSchemes())
                    ExistingRevitSchemes.Add(s);
            }
            catch { /* not fatal if none exist */ }
        }
    }
}
