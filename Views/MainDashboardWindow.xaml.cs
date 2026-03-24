using Autodesk.Revit.UI;
using ColorSchemeAddin.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ColorSchemeAddin.Views
{
    public partial class MainDashboardWindow : Window
    {
        private MainDashboardViewModel? _vm;

        private readonly CreateSchemeView _createView = new();
        private readonly ManageSchemeView _manageView = new();
        private readonly ApplySchemeView  _applyView  = new();

        private readonly SolidColorBrush _activeColor   = new(Color.FromRgb(220, 37, 0));
        private readonly SolidColorBrush _inactiveColor = new(Color.FromRgb(176, 184, 196));

        public MainDashboardWindow(UIApplication uiApp)
        {
            InitializeComponent();
            try
            {
                _vm = new MainDashboardViewModel(uiApp);
                DataContext = _vm;

                _createView.DataContext = _vm.CreateVM;
                _manageView.DataContext = _vm.ManageVM;
                _applyView.DataContext  = _vm.ApplyVM;

                // ── Listen for SelectedTabIndex changes on the ViewModel ──
                // This lets any ViewModel call NavigateToManage/Create/Apply
                // and have the window respond by actually swapping the view.
                _vm.PropertyChanged += OnVmPropertyChanged;

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

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainDashboardViewModel.SelectedTabIndex)
                && _vm != null)
            {
                ShowTab(_vm.SelectedTabIndex);
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
            UpdateNavHighlight(index);
        }

        private void UpdateNavHighlight(int activeIndex)
        {
            BtnCreate.Foreground = activeIndex == 0 ? _activeColor : _inactiveColor;
            BtnManage.Foreground = activeIndex == 1 ? _activeColor : _inactiveColor;
            BtnApply.Foreground  = activeIndex == 2 ? _activeColor : _inactiveColor;

            BtnCreate.FontWeight = activeIndex == 0 ? FontWeights.SemiBold : FontWeights.Normal;
            BtnManage.FontWeight = activeIndex == 1 ? FontWeights.SemiBold : FontWeights.Normal;
            BtnApply.FontWeight  = activeIndex == 2 ? FontWeights.SemiBold : FontWeights.Normal;
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
