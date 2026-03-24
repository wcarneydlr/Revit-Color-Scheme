using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Text;

namespace ColorSchemeAddin.Commands
{
    /// <summary>
    /// Temporary diagnostic command — dumps all ElementId parameters on the
    /// active view so we can find exactly which one holds the color fill scheme.
    /// Run from the DLR Group ribbon while an Area Plan is the active view.
    /// Remove this command once the parameter name is confirmed.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DiagnosticCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var doc     = commandData.Application.ActiveUIDocument.Document;
            var view    = doc.ActiveView;
            var sb      = new StringBuilder();

            sb.AppendLine($"Active view: {view.Name}  (ViewType: {view.ViewType})");
            sb.AppendLine($"ViewTemplateId: {view.ViewTemplateId}");
            sb.AppendLine();

            // Get all ColorFillScheme element ids in the document
            var schemeIds = new System.Collections.Generic.HashSet<ElementId>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ColorFillScheme))
                    .ToElementIds());

            sb.AppendLine($"ColorFillSchemes in doc: {schemeIds.Count}");
            sb.AppendLine();

            // Dump every parameter on the view
            sb.AppendLine("=== All parameters on active view ===");
            foreach (Parameter p in view.Parameters)
            {
                try
                {
                    string val = p.StorageType switch
                    {
                        StorageType.ElementId => p.AsElementId().ToString(),
                        StorageType.String    => p.AsString() ?? "(null)",
                        StorageType.Integer   => p.AsInteger().ToString(),
                        StorageType.Double    => p.AsDouble().ToString("F3"),
                        _                     => p.StorageType.ToString()
                    };

                    string isScheme = p.StorageType == StorageType.ElementId &&
                                      schemeIds.Contains(p.AsElementId())
                                      ? "  *** COLOR FILL SCHEME ***" : "";

                    sb.AppendLine($"  [{p.Definition.Name}] = {val}{isScheme}");
                }
                catch { }
            }

            // Also check if view has a template and dump template params
            if (view.ViewTemplateId != ElementId.InvalidElementId)
            {
                var tmpl = doc.GetElement(view.ViewTemplateId) as View;
                if (tmpl != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"=== View template: {tmpl.Name} ===");
                    foreach (Parameter p in tmpl.Parameters)
                    {
                        try
                        {
                            if (p.StorageType != StorageType.ElementId) continue;
                            var id = p.AsElementId();
                            if (schemeIds.Contains(id))
                                sb.AppendLine(
                                    $"  [TEMPLATE] [{p.Definition.Name}] = {id}  *** COLOR FILL SCHEME ***");
                        }
                        catch { }
                    }
                }
            }

            // Show result — use TaskDialog expandable section
            var td = new TaskDialog("Color Fill Scheme Diagnostic");
            td.MainInstruction = $"View: {view.Name}  ({view.ViewType})";

            // Find the scheme hits for summary
            var hits = new System.Collections.Generic.List<string>();
            foreach (Parameter p in view.Parameters)
            {
                try
                {
                    if (p.StorageType == StorageType.ElementId &&
                        schemeIds.Contains(p.AsElementId()))
                        hits.Add($"{p.Definition.Name} = {p.AsElementId()}");
                }
                catch { }
            }

            td.MainContent = hits.Count > 0
                ? "Color fill scheme parameters found:\n" + string.Join("\n", hits)
                : "No color fill scheme parameters found on this view.\n\n" +
                  "Check expanded content for all parameters.";

            td.ExpandedContent = sb.ToString();
            td.Show();

            return Result.Succeeded;
        }
    }
}
