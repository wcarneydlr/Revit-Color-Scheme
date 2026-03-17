using Autodesk.Revit.DB;
using ColorSchemeAddin.Services;
using System.Windows;

namespace ColorSchemeAddin.Views
{
    public partial class SchemePickerDialog : Window
    {
        public ColorFillScheme? SelectedScheme { get; private set; }

        public SchemePickerDialog(Document doc)
        {
            InitializeComponent();

            // Populate the list from the document
            var service = new ColorFillSchemeService(doc);
            SchemeList.ItemsSource = service.GetAllSchemes();
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            SelectedScheme = SchemeList.SelectedItem as ColorFillScheme;
            if (SelectedScheme == null)
            {
                MessageBox.Show("Please select a scheme from the list.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
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
