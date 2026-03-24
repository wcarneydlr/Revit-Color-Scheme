using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace ColorSchemeAddin
{
    public class App : IExternalApplication
    {
        public static string AssemblyPath { get; private set; } = string.Empty;

        public Result OnStartup(UIControlledApplication application)
        {
            AssemblyPath = Assembly.GetExecutingAssembly().Location;

            try
            {
                const string tabName = "DLR Group";
                try { application.CreateRibbonTab(tabName); } catch { }

                RibbonPanel panel = application.CreateRibbonPanel(tabName, "Color Scheme");

                // Main button
                var btnData = new PushButtonData(
                    "ColorSchemeManager",
                    "Color Scheme\nManager",
                    AssemblyPath,
                    "ColorSchemeAddin.Commands.ColorSchemeCommand")
                {
                    ToolTip = "Open the DLR Color Scheme Manager.",
                };

                string iconPath = Path.Combine(
                    Path.GetDirectoryName(AssemblyPath)!, "Resources", "ColorScheme32.png");
                if (File.Exists(iconPath))
                    btnData.LargeImage = new BitmapImage(new Uri(iconPath));

                panel.AddItem(btnData);

                // Diagnostic button — remove after confirming color fill parameter name
                var diagData = new PushButtonData(
                    "ColorSchemeDiag",
                    "Diagnose\nView",
                    AssemblyPath,
                    "ColorSchemeAddin.Commands.DiagnosticCommand")
                {
                    ToolTip = "Dumps color fill scheme parameters on the active view. " +
                              "Open an Area Plan with a color scheme applied first."
                };

                panel.AddItem(diagData);

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
