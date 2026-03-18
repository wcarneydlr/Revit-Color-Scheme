using Autodesk.Revit.UI;
using ColorSchemeAddin.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace ColorSchemeAddin.Views
{
    public partial class MainDashboardWindow : Window
    {
        private MainDashboardViewModel? _vm;

        // Pre-create all three views once
        private readonly CreateSchemeView _createView = new();
        private readonly ManageSchemeView _manageView = new();
        private readonly ApplySchemeView  _applyView  = new();

        public MainDashboardWindow(UIApplication uiApp)
        {
            InitializeComponent();
            try
            {
                _vm = new MainDashboardViewModel(uiApp);
                DataContext = _vm;

                // Wire DataContexts
                _createView.DataContext = _vm.CreateVM;
                _manageView.DataContext = _vm.ManageVM;
                _applyView.DataContext  = _vm.ApplyVM;

                // Show the correct starting tab
                ShowTab(_vm.SelectedTabIndex);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException;
                string innerMsg = inner != null
                    ? $"\n\nInner: {inner.GetType().Name}: {inner.Message}"
                    : string.Empty;
                MessageBox.Show(
                    $"{ex.GetType().Name}\n{ex.Message}{innerMsg}\n\n{ex.StackTrace}",
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowTab(int index)
        {
            MainContent.Content = index switch
            {
                0 => (UserControl)_createView,
                1 => _manageView,
                2 => _applyView,
                _ => _createView
            };
        }

        private void NavCreate_Click(object sender, RoutedEventArgs e)
        {
            _vm?.NavigateToCreate();
            ShowTab(0);
        }

        private void NavManage_Click(object sender, RoutedEventArgs e)
        {
            _vm?.NavigateToManage();
            ShowTab(1);
        }

        private void NavApply_Click(object sender, RoutedEventArgs e)
        {
            _vm?.NavigateToApply();
            ShowTab(2);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
