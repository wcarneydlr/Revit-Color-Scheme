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
using System.Windows.Threading;
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

        // ── Scratch entry for "create from scratch" ────────────────────────
        [ObservableProperty] private ColorSchemeModel _scratchScheme = new();
        [ObservableProperty] private string _scratchSchemeName = string.Empty;

        public static string[] AvailableParameters => new[]
        {
            "Department", "Name", "Occupancy", "Space Type",
            "Phase", "Level", "Function", "Comments"
        };

        public CreateSchemeViewModel(Document doc, MainDashboardViewModel main)
        {
            _doc  = doc;
            _main = main;
            ResetScratchScheme();
            // Defer Revit API read until after window is loaded
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    try { LoadExistingRevitSchemes(); } catch { }
                }));
        }

        // ── Excel import commands ──────────────────────────────────────────

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
                catch (Exception ex)
                {
                    ShowError(ex);
                }
            }
        }

        [RelayCommand]
        private void CreateFromImport()
        {
            if (SelectedImportedScheme == null)
            {
                MessageBox.Show("Please import an Excel file and select a scheme.",
                    "No Scheme Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            TryCreateOrUpdate(SelectedImportedScheme);
        }

        // ── From scratch commands ──────────────────────────────────────────

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
            if (entry != null)
                ScratchScheme.Entries.Remove(entry);
        }

        [RelayCommand]
        private void CreateFromScratch()
        {
            if (string.IsNullOrWhiteSpace(ScratchSchemeName))
            {
                MessageBox.Show("Please enter a name for the scheme.",
                    "Name Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (ScratchScheme.Entries.Count == 0)
            {
                MessageBox.Show("Please add at least one color entry.",
                    "No Entries", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ScratchScheme.Name = ScratchSchemeName;
            ScratchScheme.ParameterName = ParameterName;
            ScratchScheme.ApplyToRooms = TargetRooms;
            ScratchScheme.ApplyToAreas = TargetAreas;

            TryCreateOrUpdate(ScratchScheme);
            ResetScratchScheme();
        }

        // ── From existing Revit scheme command ─────────────────────────────

        [RelayCommand]
        private void ImportFromRevitScheme()
        {
            if (SelectedRevitScheme == null)
            {
                MessageBox.Show("Please select an existing Revit color scheme.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var service = new ColorFillSchemeService(_doc);
            var model = service.ToModel(SelectedRevitScheme);
            model.ApplyToRooms   = TargetRooms;
            model.ApplyToAreas   = TargetAreas;
            model.ParameterName  = ParameterName;

            ImportedSchemes.Add(model);
            SelectedImportedScheme = model;
            LastResult = $"Loaded '{model.Name}' from Revit ({model.Entries.Count} entries). " +
                         "Review then click Create Scheme.";
            _main.SetStatus(LastResult);
        }

        // ── Shared create/update ───────────────────────────────────────────

        private void TryCreateOrUpdate(ColorSchemeModel model)
        {
            try
            {
                _main.SetStatus($"Creating scheme: {model.Name}…", true);
                var service = new ColorFillSchemeService(_doc);

                var existing = service.FindByName(model.Name);
                if (existing != null)
                {
                    var result = MessageBox.Show(
                        $"A scheme named '{model.Name}' already exists.\nUpdate it with these colors?",
                        "Scheme Exists", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        service.UpdateScheme(existing, model);
                        LastResult = $"Updated '{model.Name}' with {model.Entries.Count} entries.";
                    }
                    else return;
                }
                else
                {
                    var catId = TargetAreas
                        ? new ElementId(BuiltInCategory.OST_Areas)
                        : new ElementId(BuiltInCategory.OST_Rooms);
                    service.CreateScheme(model, catId);
                    LastResult = $"Created '{model.Name}' with {model.Entries.Count} entries.";
                }

                _main.SetStatus(LastResult);
                _main.NavigateToManage();
            }
            catch (Exception ex) { ShowError(ex); }
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
            catch (Exception ex) { ShowError(ex); }
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
            catch { }
        }

        private void ResetScratchScheme()
        {
            ScratchScheme = new ColorSchemeModel();
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
