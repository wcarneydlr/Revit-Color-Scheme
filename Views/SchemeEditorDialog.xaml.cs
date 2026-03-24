using Autodesk.Revit.DB;
using ColorSchemeAddin.Models;
using ColorSchemeAddin.Services;
using ColorSchemeAddin.ViewModels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace ColorSchemeAddin.Views
{
    public partial class SchemeEditorDialog : Window
    {
        private readonly SchemeEditorViewModel _vm;
        private readonly ColorSchemeModel _model;
        private readonly Document _doc;

        public bool Saved { get; private set; }

        public SchemeEditorDialog(ColorSchemeModel model, Document doc)
        {
            InitializeComponent();
            _model = model;
            _doc   = doc;
            _vm    = new SchemeEditorViewModel(model, doc);
            DataContext = _vm;
        }

        // ── Hover pencil on scheme name ────────────────────────────────────

        private void NameDisplay_MouseEnter(object sender, MouseEventArgs e)
        {
            PencilIcon.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, System.TimeSpan.FromMilliseconds(150)));
        }

        private void NameDisplay_MouseLeave(object sender, MouseEventArgs e)
        {
            PencilIcon.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0, System.TimeSpan.FromMilliseconds(150)));
        }

        private void PencilIcon_Click(object sender, MouseButtonEventArgs e)
        {
            EnterRenameMode();
        }

        private void EnterRenameMode()
        {
            NameEditBox.Text = _vm.SchemeName;
            NameDisplayPanel.Visibility = System.Windows.Visibility.Collapsed;
            NameEditPanel.Visibility    = System.Windows.Visibility.Visible;
            NameEditBox.Focus();
            NameEditBox.SelectAll();
        }

        private void ExitRenameMode()
        {
            NameDisplayPanel.Visibility = System.Windows.Visibility.Visible;
            NameEditPanel.Visibility    = System.Windows.Visibility.Collapsed;
        }

        private void ConfirmRename_Click(object sender, RoutedEventArgs e)
            => ApplyRename();

        private void CancelRename_Click(object sender, RoutedEventArgs e)
            => ExitRenameMode();

        private void NameEditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)  ApplyRename();
            if (e.Key == Key.Escape) ExitRenameMode();
        }

        private void NameEditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Only auto-confirm if edit panel is still visible
            // (prevents double-fire when confirm button steals focus)
            if (NameEditPanel.Visibility == System.Windows.Visibility.Visible)
                ApplyRename();
        }

        private void ApplyRename()
        {
            var newName = NameEditBox.Text?.Trim();
            if (!string.IsNullOrEmpty(newName))
                _vm.PendingName = newName!;
            ExitRenameMode();
        }

        // ── Entry management ───────────────────────────────────────────────

        private void AddEntry_Click(object sender, RoutedEventArgs e)
        {
            _vm.Entries.Add(new ColorEntryModel
            {
                Value = "New Entry", ColorName = "New Entry",
                R = 128, G = 128, B = 128
            });
        }

        private void DeleteEntry_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is ColorEntryModel entry)
                _vm.Entries.Remove(entry);
        }

        // ── Color swatch picker ────────────────────────────────────────────

        private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is ColorEntryModel entry)
            {
                using var dlg = new System.Windows.Forms.ColorDialog
                {
                    Color    = System.Drawing.Color.FromArgb(entry.R, entry.G, entry.B),
                    FullOpen = true,
                    AnyColor = true
                };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    entry.R = dlg.Color.R;
                    entry.G = dlg.Color.G;
                    entry.B = dlg.Color.B;
                }
            }
        }

        // ── New parameter ──────────────────────────────────────────────────

        private void ShowNewParam_Click(object sender, RoutedEventArgs e)
            => _vm.ShowParamInput = true;

        private void CancelNewParam_Click(object sender, RoutedEventArgs e)
        {
            _vm.ShowParamInput   = false;
            _vm.NewParameterName = string.Empty;
        }

        private void CreateNewParam_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_vm.NewParameterName))
            {
                MessageBox.Show("Please enter a parameter name.",
                    "Name Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                string created = _vm.CreateParameter(_vm.NewParameterName.Trim());
                if (!_vm.AvailableParameters.Contains(created))
                    _vm.AvailableParameters.Add(created);
                _vm.ParameterName    = created;
                _vm.ShowParamInput   = false;
                _vm.NewParameterName = string.Empty;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, "Parameter Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Footer ─────────────────────────────────────────────────────────

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _model.ParameterName = _vm.ParameterName;
            _model.ApplyToRooms  = _vm.ApplyRooms;
            _model.ApplyToAreas  = _vm.ApplyAreas;

            // Apply pending rename if set
            if (!string.IsNullOrEmpty(_vm.PendingName))
                _model.Name = _vm.PendingName;

            Saved = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Saved = false;
            DialogResult = false;
            Close();
        }
    }
}
