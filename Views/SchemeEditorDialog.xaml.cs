using Autodesk.Revit.DB;
using ColorSchemeAddin.Models;
using System.Windows;
using System.Windows.Forms;

namespace ColorSchemeAddin.Views
{
    public partial class SchemeEditorDialog : Window
    {
        private readonly Document _doc;
        public ColorSchemeModel Model { get; }

        public System.Collections.ObjectModel.ObservableCollection<ColorEntryModel> Entries
            => Model.Entries;

        public SchemeEditorDialog(ColorSchemeModel model, Document doc)
        {
            _doc = doc;
            Model = model;
            DataContext = this;
            InitializeComponent();
        }

        private void PickColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn &&
                btn.Tag is ColorEntryModel entry)
            {
                using var dlg = new ColorDialog
                {
                    Color = System.Drawing.Color.FromArgb(entry.R, entry.G, entry.B),
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

        private void AddEntry_Click(object sender, RoutedEventArgs e)
        {
            Model.Entries.Add(new ColorEntryModel
            {
                Value = "New Entry", ColorName = "New Entry",
                R = 128, G = 128, B = 128
            });
        }

        private void RemoveEntry_Click(object sender, RoutedEventArgs e)
        {
            if (Model.Entries.Count > 0)
                Model.Entries.RemoveAt(Model.Entries.Count - 1);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
