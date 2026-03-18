using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ColorSchemeAddin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Windows.Threading;

namespace ColorSchemeAddin.ViewModels
{
    public partial class MainDashboardViewModel : ObservableObject
    {
        private readonly UIApplication _uiApp;
        private readonly Document _doc;

        public CreateSchemeViewModel CreateVM { get; }
        public ManageSchemeViewModel ManageVM { get; }
        public ApplySchemeViewModel  ApplyVM  { get; }

        [ObservableProperty] private int    _selectedTabIndex;
        [ObservableProperty] private string _statusMessage = "Ready";
        [ObservableProperty] private bool   _isBusy;

        public string DocumentTitle => _doc?.Title ?? string.Empty;
        public string RevitVersion  => $"Revit {_uiApp?.Application?.VersionNumber}";

        public MainDashboardViewModel(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _doc   = uiApp.ActiveUIDocument.Document;

            // Build child VMs — no Revit API calls in their constructors
            CreateVM = new CreateSchemeViewModel(_doc, this);
            ManageVM = new ManageSchemeViewModel(_doc, this);
            ApplyVM  = new ApplySchemeViewModel(_doc, this);

            // Determine opening tab
            try
            {
                bool firstLaunch = !ExtensibleStorageService.HasBeenRun(_doc);
                SelectedTabIndex = firstLaunch ? 0 : 1;

                if (firstLaunch)
                {
                    // Defer the transaction until after the window is shown
                    // Use Dispatcher.CurrentDispatcher — safe in Revit context
                    Dispatcher.CurrentDispatcher.BeginInvoke(
                        DispatcherPriority.Loaded,
                        new Action(() =>
                        {
                            try { ExtensibleStorageService.MarkAsRun(_doc); }
                            catch { /* non-fatal */ }
                        }));
                }
                else
                {
                    Dispatcher.CurrentDispatcher.BeginInvoke(
                        DispatcherPriority.Loaded,
                        new Action(() =>
                        {
                            try { ManageVM.RefreshSchemes(); }
                            catch { /* non-fatal */ }
                        }));
                }
            }
            catch
            {
                SelectedTabIndex = 0;
            }
        }

        [RelayCommand] public void NavigateToCreate() => SelectedTabIndex = 0;

        [RelayCommand]
        public void NavigateToManage()
        {
            SelectedTabIndex = 1;
            try { ManageVM.RefreshSchemes(); } catch { }
        }

        [RelayCommand]
        public void NavigateToApply()
        {
            SelectedTabIndex = 2;
            try { ApplyVM.RefreshSchemes(); } catch { }
        }

        public void SetStatus(string message, bool busy = false)
        {
            StatusMessage = message;
            IsBusy = busy;
        }
    }
}
