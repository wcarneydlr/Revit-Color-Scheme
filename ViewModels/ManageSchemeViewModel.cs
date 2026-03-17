using Autodesk.Revit.DB;
using ColorSchemeAddin.Models;
using ColorSchemeAddin.Services;
using ColorSchemeAddin.Views;
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
    public partial class ManageSchemeViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly MainDashboardViewModel _main;

        [ObservableProperty] private ObservableCollection<ColorSchemeRowModel> _schemeRows = new();
        [ObservableProperty] private ColorSchemeRowModel? _selectedRow;
        [ObservableProperty] private string _filterText = string.Empty;

        public ManageSchemeViewModel(Document doc, MainDashboardViewModel main)
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
                SchemeRows.Clear();
                var service = new ColorFillSchemeService(_doc);
                foreach (var scheme in service.GetAllSchemes())
                {
                    var model = service.ToModel(scheme);
                    SchemeRows.Add(new ColorSchemeRowModel
                    {
                        RevitScheme  = scheme,
                        Name         = scheme.Name,
                        EntryCount   = model.Entries.Count,
                        CategoryName = GetCategoryName(scheme),
                        Model        = model
                    });
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
                    row.EntryCount = row.Model.Entries.Count;
                    _main.SetStatus($"Updated scheme '{row.Name}'.");
                    RefreshSchemes();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void ExportSelected()
        {
            var toExport = SchemeRows.Where(r => r.IsSelected).Select(r => r.Model).ToList();
            if (!toExport.Any())
            {
                MessageBox.Show("Check at least one scheme to export.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Export Color Schemes to Excel",
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = "DLR_Color_Schemes_Export.xlsx"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    ExcelService.ExportToFile(toExport, dlg.FileName);
                    _main.SetStatus($"Exported {toExport.Count} scheme(s) to {Path.GetFileName(dlg.FileName)}.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void ExportAll()
        {
            if (!SchemeRows.Any())
            {
                MessageBox.Show("No schemes to export.", "Empty", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Export All Color Schemes to Excel",
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = "DLR_Color_Schemes_All.xlsx"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    ExcelService.ExportToFile(SchemeRows.Select(r => r.Model), dlg.FileName);
                    _main.SetStatus($"Exported {SchemeRows.Count} scheme(s) to {Path.GetFileName(dlg.FileName)}.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void SelectAll(bool selected)
        {
            foreach (var row in SchemeRows) row.IsSelected = selected;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private string GetCategoryName(ColorFillScheme scheme)
        {
            try
            {
                var catId = scheme.CategoryId;
                var cat = Category.GetCategory(_doc, catId);
                return cat?.Name ?? catId.ToString();
            }
            catch { return "Unknown"; }
        }
    }

    /// <summary>One row in the Manage grid.</summary>
    public partial class ColorSchemeRowModel : ObservableObject
    {
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private int _entryCount;

        public ColorFillScheme RevitScheme { get; set; } = null!;
        public string Name { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public ColorSchemeModel Model { get; set; } = null!;
    }
}
