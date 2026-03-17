using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace ColorSchemeAddin
{
    /// <summary>
    /// Entry point: creates the DLR Group ribbon tab and Color Scheme panel.
    /// </summary>
    public class App : IExternalApplication
    {
        public static string AssemblyPath { get; private set; } = string.Empty;

        public Result OnStartup(UIControlledApplication application)
        {
            AssemblyPath = Assembly.GetExecutingAssembly().Location;

            try
            {
                // Create (or get existing) DLR Group tab
                const string tabName = "DLR Group";
                try { application.CreateRibbonTab(tabName); } catch { /* tab already exists */ }

                // Create the Color Scheme panel
                RibbonPanel panel = application.CreateRibbonPanel(tabName, "Color Scheme");

                // Main dashboard button
                var btnData = new PushButtonData(
                    "ColorSchemeManager",
                    "Color Scheme\nManager",
                    AssemblyPath,
                    "ColorSchemeAddin.Commands.ColorSchemeCommand")
                {
                    ToolTip = "Open the DLR Color Scheme Manager to create, manage, and apply color fill schemes.",
                    LongDescription = "Create color fill schemes from Excel templates, manage existing schemes, " +
                                      "apply them to rooms/areas, generate materials, view filters, and view templates.",
                };

                // Try to load ribbon icon from Resources folder
                string iconPath = Path.Combine(Path.GetDirectoryName(AssemblyPath)!, "Resources", "ColorScheme32.png");
                if (File.Exists(iconPath))
                {
                    btnData.LargeImage = new BitmapImage(new Uri(iconPath));
                }

                panel.AddItem(btnData);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("DLR Color Scheme — Startup Error", ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }
}
