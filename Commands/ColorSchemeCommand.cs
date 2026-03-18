using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ColorSchemeAddin.Views;
using System;
using System.Text;

namespace ColorSchemeAddin.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ColorSchemeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = commandData.Application;
                var uiDoc = uiApp.ActiveUIDocument;

                if (uiDoc == null)
                {
                    TaskDialog.Show("Color Scheme Manager",
                        "Please open a Revit project document first.");
                    return Result.Cancelled;
                }

                if (uiDoc.Document.IsFamilyDocument)
                {
                    TaskDialog.Show("Color Scheme Manager",
                        "Color Scheme Manager is not available in Family documents.");
                    return Result.Cancelled;
                }

                var window = new MainDashboardWindow(uiApp);
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                var sb = new StringBuilder();
                sb.AppendLine(ex.GetType().FullName);
                sb.AppendLine(ex.Message);

                var inner = ex.InnerException;
                int depth = 0;
                while (inner != null && depth < 5)
                {
                    sb.AppendLine($"--- Inner {depth + 1} ---");
                    sb.AppendLine(inner.GetType().FullName);
                    sb.AppendLine(inner.Message);
                    inner = inner.InnerException;
                    depth++;
                }

                sb.AppendLine();
                sb.AppendLine("--- Stack ---");
                sb.AppendLine(ex.StackTrace);

                var td = new TaskDialog("Color Scheme Manager — Error");
                td.MainInstruction = $"{ex.GetType().Name}: {ex.Message}";
                td.ExpandedContent = sb.ToString();
                td.Show();

                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
