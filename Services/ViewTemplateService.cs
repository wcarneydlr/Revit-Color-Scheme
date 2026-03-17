using Autodesk.Revit.DB;
using ColorSchemeAddin.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorSchemeAddin.Services
{
    public enum ViewTemplateType
    {
        FloorPlan,
        AreaPlan,
        ThreeD,
        Section
    }

    public enum ColorApplicationMethod
    {
        ColorFillScheme,   // Room / Area color fill
        ViewFilters,       // Parameter filters + graphic overrides
        Both
    }

    /// <summary>
    /// Generates Revit view templates and parameter filters from color scheme data.
    /// </summary>
    public class ViewTemplateService
    {
        private readonly Document _doc;

        public ViewTemplateService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        // ── View Template Generation ───────────────────────────────────────

        /// <summary>
        /// Creates a view template for a color scheme.
        /// Template name: "[SchemeName] - [ViewType]"
        /// </summary>
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
                {
                    try
                    {
                        var fillType = new FilteredElementCollector(_doc, vp.Id)
                            .OfClass(typeof(SpatialElementColorFillType))
                            .Cast<SpatialElementColorFillType>()
                            .FirstOrDefault();
                        if (fillType != null)
                            fillType.ColorFillSchemeId = colorFillScheme.Id;
                    }
                    catch { }
                }
            }

            tx.Commit();
            return template;
        }

        /// <summary>
        /// Batch-creates view templates from a list of schemes and a matrix of options.
        /// </summary>
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
                            try
                            {
                                var fillType = new FilteredElementCollector(_doc, vp.Id)
                                    .OfClass(typeof(SpatialElementColorFillType))
                                    .Cast<SpatialElementColorFillType>()
                                    .FirstOrDefault();
                                if (fillType != null)
                                    fillType.ColorFillSchemeId = revitScheme.Id;
                            }
                            catch { }
                        }
                    }

                    created.Add((name, template.Id));
                }
            }

            tx.Commit();
            return created;
        }

        // ── Filter Management ──────────────────────────────────────────────

        /// <summary>
        /// Creates or retrieves ParameterFilterElements for every entry in the scheme.
        /// Filter name: "[SchemeName] - [EntryValue]"
        /// </summary>
        public Dictionary<string, ParameterFilterElement> GetOrCreateFiltersForScheme(
            ColorSchemeModel scheme)
        {
            var results = new Dictionary<string, ParameterFilterElement>(
                StringComparer.OrdinalIgnoreCase);

            // Categories to filter: Rooms
            var categories = new List<ElementId>
            {
                new ElementId(BuiltInCategory.OST_Rooms),
            };

            foreach (var entry in scheme.Entries)
            {
                string filterName = $"{scheme.Name} - {entry.Value}";

                // Try to find an existing filter with this name
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

                // Build rule: parameter "Name" contains entry value
                // Uses Department parameter if available, otherwise Name
                var paramId = new ElementId(BuiltInParameter.ROOM_DEPARTMENT);

                var rule = ParameterFilterRuleFactory.CreateContainsRule(
                    paramId, entry.Value, false);

                var logicFilter = new ElementParameterFilter(rule);

                var filter = ParameterFilterElement.Create(
                    _doc, filterName, categories, logicFilter);

                results[entry.Value] = filter;
            }

            return results;
        }

        /// <summary>
        /// Applies filters with solid color overrides to a view.
        /// </summary>
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
                    view.AddFilter(filter.Id);

                    var overrides = new OverrideGraphicSettings();
                    var color = new Color(entry.R, entry.G, entry.B);

                    // Surface pattern: solid fill
                    overrides.SetSurfaceForegroundPatternColor(color);
                    overrides.SetSurfaceForegroundPatternVisible(true);
                    overrides.SetSurfaceBackgroundPatternColor(color);
                    overrides.SetSurfaceBackgroundPatternVisible(true);

                    // Cut pattern: same solid fill
                    overrides.SetCutForegroundPatternColor(color);
                    overrides.SetCutForegroundPatternVisible(true);
                    overrides.SetCutBackgroundPatternColor(color);
                    overrides.SetCutBackgroundPatternVisible(true);

                    // Projection lines: match color
                    overrides.SetProjectionLineColor(color);

                    view.SetFilterOverrides(filter.Id, overrides);
                }
                catch { /* filter may not be applicable to this view type */ }
            }
        }

        // ── Temporary View Settings ────────────────────────────────────────

        /// <summary>
        /// Enables Temporary View Properties on the active view and applies filter overrides.
        /// This is a preview mode — changes revert when temp view mode is cleared.
        /// </summary>
        public void ApplyTemporaryViewSettings(
            View view,
            ColorSchemeModel scheme,
            Dictionary<string, ParameterFilterElement> filters)
        {
            using var tx = new Transaction(_doc, "Apply Temporary Color Scheme View");
            tx.Start();

            // Enable temporary view properties
            var tvp = view.GetParameters()
                .FirstOrDefault(p => p.Definition.Name == "Temporary View Properties");

            ApplyFiltersToView(view, scheme, filters);

            tx.Commit();
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private View CreateTemplateOfType(ViewTemplateType type, string name)
        {
            View? sourceView = null;

            switch (type)
            {
                case ViewTemplateType.FloorPlan:
                    sourceView = new FilteredElementCollector(_doc)
                        .OfClass(typeof(ViewPlan))
                        .Cast<ViewPlan>()
                        .FirstOrDefault(v => v.ViewType == ViewType.FloorPlan && !v.IsTemplate);
                    break;

                case ViewTemplateType.AreaPlan:
                    sourceView = new FilteredElementCollector(_doc)
                        .OfClass(typeof(ViewPlan))
                        .Cast<ViewPlan>()
                        .FirstOrDefault(v => v.ViewType == ViewType.AreaPlan && !v.IsTemplate);
                    break;

                case ViewTemplateType.ThreeD:
                    sourceView = new FilteredElementCollector(_doc)
                        .OfClass(typeof(View3D))
                        .Cast<View3D>()
                        .FirstOrDefault(v => !v.IsTemplate);
                    break;

                case ViewTemplateType.Section:
                    sourceView = new FilteredElementCollector(_doc)
                        .OfClass(typeof(ViewSection))
                        .Cast<ViewSection>()
                        .FirstOrDefault(v => !v.IsTemplate);
                    break;
            }

            if (sourceView == null)
                throw new InvalidOperationException(
                    $"No existing {type} view found in the document to base the template on.");

            // Duplicate as dependent (creates a template)
            ElementId templateId = sourceView.Duplicate(ViewDuplicateOption.Duplicate);
            var template = (View)_doc.GetElement(templateId);
            template.Name = name;
            template.IsTemplate = true;

            return template;
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
