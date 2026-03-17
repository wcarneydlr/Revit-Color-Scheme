using Autodesk.Revit.UI;
using ColorSchemeAddin.ViewModels;
using System.Windows;

namespace ColorSchemeAddin.Views
{
    public partial class MainDashboardWindow : Window
    {
        public MainDashboardWindow(UIApplication uiApp)
        {
            InitializeComponent();
            DataContext = new MainDashboardViewModel(uiApp);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
