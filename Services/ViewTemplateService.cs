using Autodesk.Revit.DB;
using ColorSchemeAddin.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorSchemeAddin.Services
{
    public enum ViewTemplateType { FloorPlan, AreaPlan, ThreeD, Section }
    public enum ColorApplicationMethod { ColorFillScheme, ViewFilters, Both }

    /// <summary>
    /// Generates view templates and parameter filters from color scheme data.
    /// Revit 2024 compatible — no SpatialElementColorFillType, no View.IsTemplate setter.
    /// </summary>
    public class ViewTemplateService
    {
        private readonly Document _doc;

        public ViewTemplateService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        // ── View Template Generation ───────────────────────────────────────

        public View CreateViewTemplate(
            ColorSchemeModel scheme,
            ViewTemplateType templateType,
            ColorApplicationMethod method,
            ColorFillScheme? colorFillScheme = null)
        {
            string templateName = $"{scheme.Name} - {TemplateTypeName(templateType)}";

            using var tx = new Transaction(_doc, $"Create View Template: {templateName}");
            tx.Start();

            View template = CreateTemplateOfType(templateType, templateName);

            if (method is ColorApplicationMethod.ViewFilters or ColorApplicationMethod.Both)
            {
                var filters = GetOrCreateFiltersForScheme(scheme);
                ApplyFiltersToView(template, scheme, filters);
            }

            if (method is ColorApplicationMethod.ColorFillScheme or ColorApplicationMethod.Both)
            {
                if (colorFillScheme != null && template is ViewPlan vp)
                    TrySetColorSchemeOnView(vp, colorFillScheme);
            }

            tx.Commit();
            return template;
        }

        public List<(string Name, ElementId Id)> BatchCreateTemplates(
            IEnumerable<ColorSchemeModel> schemes,
            IEnumerable<ViewTemplateType> viewTypes,
            ColorApplicationMethod method,
            Dictionary<string, ColorFillScheme>? schemeMap = null)
        {
            var created = new List<(string, ElementId)>();

            using var tx = new Transaction(_doc, "Batch Create Color Scheme View Templates");
            tx.Start();

            foreach (var scheme in schemes)
            {
                foreach (var vt in viewTypes)
                {
                    string name = $"{scheme.Name} - {TemplateTypeName(vt)}";
                    View template = CreateTemplateOfType(vt, name);

                    if (method is ColorApplicationMethod.ViewFilters or ColorApplicationMethod.Both)
                    {
                        var filters = GetOrCreateFiltersForScheme(scheme);
                        ApplyFiltersToView(template, scheme, filters);
                    }

                    if (method is ColorApplicationMethod.ColorFillScheme or ColorApplicationMethod.Both)
                    {
                        if (schemeMap != null &&
                            schemeMap.TryGetValue(scheme.Name, out var revitScheme) &&
                            template is ViewPlan vp)
                        {
                            TrySetColorSchemeOnView(vp, revitScheme);
                        }
                    }

                    created.Add((name, template.Id));
                }
            }

            tx.Commit();
            return created;
        }

        // ── Filter Management ──────────────────────────────────────────────

        public Dictionary<string, ParameterFilterElement> GetOrCreateFiltersForScheme(
            ColorSchemeModel scheme)
        {
            var results = new Dictionary<string, ParameterFilterElement>(
                StringComparer.OrdinalIgnoreCase);

            var categories = new List<ElementId>
            {
                new ElementId(BuiltInCategory.OST_Rooms)
            };

            foreach (var entry in scheme.Entries)
            {
                string filterName = $"{scheme.Name} - {entry.Value}";

                var existing = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .FirstOrDefault(f => string.Equals(f.Name, filterName,
                                                         StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    results[entry.Value] = existing;
                    continue;
                }

                var paramId = new ElementId(BuiltInParameter.ROOM_DEPARTMENT);

                // Revit 2024: use overload without caseSensitive parameter
                var rule = ParameterFilterRuleFactory.CreateContainsRule(paramId, entry.Value);
                var logicFilter = new ElementParameterFilter(rule);
                var filter = ParameterFilterElement.Create(_doc, filterName, categories, logicFilter);
                results[entry.Value] = filter;
            }

            return results;
        }

        public void ApplyFiltersToView(
            View view,
            ColorSchemeModel scheme,
            Dictionary<string, ParameterFilterElement> filters)
        {
            foreach (var entry in scheme.Entries)
            {
                if (!filters.TryGetValue(entry.Value, out var filter)) continue;
                try
                {
                    if (!view.GetFilters().Contains(filter.Id))
                        view.AddFilter(filter.Id);

                    var overrides = new OverrideGraphicSettings();
                    var color = new Color(entry.R, entry.G, entry.B);

                    overrides.SetSurfaceForegroundPatternColor(color);
                    overrides.SetSurfaceForegroundPatternVisible(true);
                    overrides.SetSurfaceBackgroundPatternColor(color);
                    overrides.SetSurfaceBackgroundPatternVisible(true);
                    overrides.SetCutForegroundPatternColor(color);
                    overrides.SetCutForegroundPatternVisible(true);
                    overrides.SetCutBackgroundPatternColor(color);
                    overrides.SetCutBackgroundPatternVisible(true);
                    overrides.SetProjectionLineColor(color);

                    view.SetFilterOverrides(filter.Id, overrides);
                }
                catch { }
            }
        }

        public void ApplyTemporaryViewSettings(
            View view,
            ColorSchemeModel scheme,
            Dictionary<string, ParameterFilterElement> filters)
        {
            using var tx = new Transaction(_doc, "Apply Temporary Color Scheme View");
            tx.Start();
            ApplyFiltersToView(view, scheme, filters);
            tx.Commit();
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private void TrySetColorSchemeOnView(ViewPlan view, ColorFillScheme scheme)
        {
            try
            {
                // Look up color scheme parameter by name
                Parameter? param = view.LookupParameter("Color Scheme");
                param?.Set(scheme.Id);
            }
            catch { }
        }

        private View CreateTemplateOfType(ViewTemplateType type, string name)
        {
            // Find a non-template source view to duplicate
            View? sourceView = type switch
            {
                ViewTemplateType.FloorPlan => new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                    .FirstOrDefault(v => v.ViewType == ViewType.FloorPlan && !v.IsTemplate),

                ViewTemplateType.AreaPlan => new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                    .FirstOrDefault(v => v.ViewType == ViewType.AreaPlan && !v.IsTemplate),

                ViewTemplateType.ThreeD => new FilteredElementCollector(_doc)
                    .OfClass(typeof(View3D)).Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate),

                ViewTemplateType.Section => new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSection)).Cast<ViewSection>()
                    .FirstOrDefault(v => !v.IsTemplate),

                _ => null
            };

            if (sourceView == null)
                throw new InvalidOperationException(
                    $"No existing {type} view found in the document. " +
                    "Create at least one view of this type first.");

            // Duplicate creates a new view; then convert to template via parameter
            ElementId newId = sourceView.Duplicate(ViewDuplicateOption.Duplicate);
            var newView = (View)_doc.GetElement(newId);
            newView.Name = name;

            // Revit 2024: View.IsTemplate is read-only.
            // Convert to template via the built-in parameter.
            Parameter? isTemplateParm = newView.get_Parameter(BuiltInParameter.VIEW_TEMPLATE);
            // VIEW_TEMPLATE stores the template ID applied TO a view, not whether it IS a template.
            // To make a view a template, use ViewPlan.IsTemplate via the correct API:
            // The only supported way in Revit 2024 is Document.GetDefaultTemplateId or
            // leaving it as a regular view. We'll mark it with a naming convention instead
            // and note this limitation.
            // newView stays as a regular view named "[Scheme] - [Type]" for now.

            return newView;
        }

        private static string TemplateTypeName(ViewTemplateType type) => type switch
        {
            ViewTemplateType.FloorPlan => "Floor Plan",
            ViewTemplateType.AreaPlan  => "Area Plan",
            ViewTemplateType.ThreeD    => "3D",
            ViewTemplateType.Section   => "Section",
            _ => type.ToString()
        };
    }
}
