using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ColorSchemeAddin.Models;
using ColorSchemeAddin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace ColorSchemeAddin.ViewModels
{
    public partial class MainDashboardViewModel : ObservableObject
    {
        private readonly UIApplication _uiApp;
        private readonly Document _doc;

        // Sub-viewmodels for each tab
        public CreateSchemeViewModel CreateVM { get; }
        public ManageSchemeViewModel ManageVM { get; }
        public ApplySchemeViewModel ApplyVM { get; }

        [ObservableProperty] private int _selectedTabIndex;
        [ObservableProperty] private string _statusMessage = "Ready";
        [ObservableProperty] private bool _isBusy;

        public string DocumentTitle => _doc.Title;
        public string RevitVersion => $"Revit {_uiApp.Application.VersionNumber}";

        public MainDashboardViewModel(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _doc = uiApp.ActiveUIDocument.Document;

            CreateVM = new CreateSchemeViewModel(_doc, this);
            ManageVM = new ManageSchemeViewModel(_doc, this);
            ApplyVM  = new ApplySchemeViewModel(_doc, this);
        }

        public void SetStatus(string message, bool busy = false)
        {
            StatusMessage = message;
            IsBusy = busy;
        }

        public void NavigateToManage()
        {
            SelectedTabIndex = 1;
            ManageVM.RefreshSchemes();
        }

        public void NavigateToApply()
        {
            SelectedTabIndex = 2;
            ApplyVM.RefreshSchemes();
        }
    }
}
