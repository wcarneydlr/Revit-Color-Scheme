using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ColorSchemeAddin.Models
{
    /// <summary>
    /// Represents one complete color fill scheme with all its entries and application settings.
    /// </summary>
    public class ColorSchemeModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _category = string.Empty;
        private string _parameterName = string.Empty;
        private bool _applyToRooms;
        private bool _applyToAreas;
        private bool _applyToFloors;
        private bool _applyToGenericModels;
        private bool _applyToMasses;

        private bool _applyMaterials;
        private bool _applyViewFilters;
        private bool _applyViewTemplates;
        private string _viewTemplateType = "Floor Plan";

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        /// <summary>Revit category: Rooms, Areas, Walls, Floors, etc.</summary>
        public string Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(); }
        }

        /// <summary>The Revit parameter to drive the color fill (e.g. "Department", "Occupancy").</summary>
        public string ParameterName
        {
            get => _parameterName;
            set { _parameterName = value; OnPropertyChanged(); }
        }

        // ── Application flags ──────────────────────────────────────────────

        public bool ApplyToRooms
        {
            get => _applyToRooms;
            set { _applyToRooms = value; OnPropertyChanged(); }
        }

        public bool ApplyToAreas
        {
            get => _applyToAreas;
            set { _applyToAreas = value; OnPropertyChanged(); }
        }

        public bool ApplyToFloors
        {
            get => _applyToFloors;
            set { _applyToFloors = value; OnPropertyChanged(); }
        }

        public bool ApplyToGenericModels
        {
            get => _applyToGenericModels;
            set { _applyToGenericModels = value; OnPropertyChanged(); }
        }

        public bool ApplyToMasses
        {
            get => _applyToMasses;
            set { _applyToMasses = value; OnPropertyChanged(); }
        }

        public bool ApplyMaterials
        {
            get => _applyMaterials;
            set { _applyMaterials = value; OnPropertyChanged(); }
        }

        public bool ApplyViewFilters
        {
            get => _applyViewFilters;
            set { _applyViewFilters = value; OnPropertyChanged(); }
        }

        public bool ApplyViewTemplates
        {
            get => _applyViewTemplates;
            set { _applyViewTemplates = value; OnPropertyChanged(); }
        }

        /// <summary>Floor Plan | Area Plan | 3D | Section</summary>
        public string ViewTemplateType
        {
            get => _viewTemplateType;
            set { _viewTemplateType = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ColorEntryModel> Entries { get; set; } = new();

        // ── INotifyPropertyChanged ─────────────────────────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// One row in a color scheme: a parameter value mapped to an RGB color.
    /// </summary>
    public class ColorEntryModel : INotifyPropertyChanged
    {
        private string _value = string.Empty;
        private string _colorName = string.Empty;
        private byte _r;
        private byte _g;
        private byte _b;
        private bool _isMapped;

        /// <summary>The Revit parameter value (e.g. "Conference", "Office", "Circulation").</summary>
        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        /// <summary>Human-readable color label (e.g. "Deep Teal").</summary>
        public string ColorName
        {
            get => _colorName;
            set { _colorName = value; OnPropertyChanged(); }
        }

        public byte R
        {
            get => _r;
            set { _r = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewBrush)); }
        }

        public byte G
        {
            get => _g;
            set { _g = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewBrush)); }
        }

        public byte B
        {
            get => _b;
            set { _b = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewBrush)); }
        }

        /// <summary>True when this entry has been successfully mapped to a Revit scheme entry.</summary>
        public bool IsMapped
        {
            get => _isMapped;
            set { _isMapped = value; OnPropertyChanged(); }
        }

        /// <summary>WPF brush for the color swatch preview in the grid.</summary>
        public SolidColorBrush PreviewBrush => new SolidColorBrush(Color.FromRgb(R, G, B));

        /// <summary>Hex string representation for display.</summary>
        public string HexColor => $"#{R:X2}{G:X2}{B:X2}";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
