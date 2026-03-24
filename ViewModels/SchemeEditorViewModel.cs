using Autodesk.Revit.DB;
using ColorSchemeAddin.Models;
using ColorSchemeAddin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;

namespace ColorSchemeAddin.ViewModels
{
    /// <summary>
    /// ViewModel for SchemeEditorDialog — merges Edit Values + Edit Options.
    /// </summary>
    public partial class SchemeEditorViewModel : ObservableObject
    {
        private readonly Document _doc;

        public string SchemeName { get; }
        /// <summary>Set by the rename UI. Written to model on Save.</summary>
        public string? PendingName { get; set; }
        public ObservableCollection<ColorEntryModel> Entries { get; }

        // ── Edit Options ───────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<string> _availableParameters = new();
        [ObservableProperty] private string _parameterName = "Department";
        [ObservableProperty] private bool   _showParamInput;
        [ObservableProperty] private string _newParameterName = string.Empty;

        [ObservableProperty] private bool _applyRooms         = true;
        [ObservableProperty] private bool _applyAreas;
        [ObservableProperty] private bool _applyMasses;
        [ObservableProperty] private bool _applyGenericModels;
        [ObservableProperty] private bool _applyFloors;

        [ObservableProperty] private ObservableCollection<AreaScheme> _availableAreaSchemes = new();
        [ObservableProperty] private AreaScheme? _selectedAreaScheme;
        [ObservableProperty] private bool _createNewAreaScheme;

        public SchemeEditorViewModel(ColorSchemeModel model, Document doc)
        {
            _doc       = doc;
            SchemeName = model.Name;
            Entries    = model.Entries;

            // Seed parameter from model
            ParameterName = model.ParameterName ?? "Department";

            // Seed category checkboxes from model
            ApplyRooms         = model.ApplyToRooms;
            ApplyAreas         = model.ApplyToAreas;
            ApplyFloors        = model.ApplyToFloors;
            ApplyGenericModels = model.ApplyToGenericModels;
            ApplyMasses        = model.ApplyToMasses;

            LoadDefaultParameters();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    try { RefreshParameters(); } catch { }
                    try { LoadAreaSchemes(); }    catch { }
                }));
        }

        // ── Parameter helpers ──────────────────────────────────────────────

        public string CreateParameter(string name)
        {
            var cats = GetSelectedCategories();
            var svc  = new ParameterService(_doc);
            return svc.CreateProjectParameter(name, cats);
        }

        public IEnumerable<BuiltInCategory> GetSelectedCategories()
        {
            var list = new List<BuiltInCategory>();
            if (ApplyRooms)         list.Add(BuiltInCategory.OST_Rooms);
            if (ApplyAreas)         list.Add(BuiltInCategory.OST_Areas);
            if (ApplyMasses)        list.Add(BuiltInCategory.OST_Mass);
            if (ApplyGenericModels) list.Add(BuiltInCategory.OST_GenericModel);
            if (ApplyFloors)        list.Add(BuiltInCategory.OST_Floors);
            return list;
        }

        // ── Private helpers ────────────────────────────────────────────────

        private void LoadDefaultParameters()
        {
            AvailableParameters.Clear();
            foreach (var p in new[]
            {
                "Department","Name","Occupancy","Space Type",
                "Phase","Level","Function","Comments"
            })
                AvailableParameters.Add(p);

            if (!AvailableParameters.Contains(ParameterName))
                AvailableParameters.Add(ParameterName);
        }

        private void RefreshParameters()
        {
            try
            {
                var svc  = new ParameterService(_doc);
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
                ParameterName = AvailableParameters.Contains(current)
                    ? current : (AvailableParameters.FirstOrDefault() ?? "Department");
            }
            catch { }
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
    }
}
