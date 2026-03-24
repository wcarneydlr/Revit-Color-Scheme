using Autodesk.Revit.DB;
using ColorSchemeAddin.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorSchemeAddin.Services
{
    public enum ViewTemplateType       { FloorPlan, AreaPlan, ThreeD, Section }
    public enum ColorApplicationMethod { ColorFillScheme, ViewFilters, Both }

    /// <summary>
    /// Creates and applies view filters and templates from color scheme data.
    ///
    /// TRANSACTION RULES (Revit 2024):
    ///   ParameterFilterElement.Create() and View.CreateViewTemplate() each
    ///   require their own transaction — they cannot be nested.
    ///   All public methods manage their own transactions internally.
    /// </summary>
    public class ViewTemplateService
    {
        private readonly Document _doc;

        public ViewTemplateService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        // ── Public apply entry points ──────────────────────────────────────

        /// <summary>Apply scheme to a single view. Manages own transactions.</summary>
        public void ApplySchemeToView(View view, ColorSchemeModel scheme,
            ColorFillScheme? colorFillScheme = null)
        {
            View target = GetApplyTarget(view);

            // Tx 1: create filters
            var filters = EnsureFilters(scheme);

            // Tx 2: apply
            using var tx = new Transaction(_doc, $"DLR — Apply: {scheme.Name}");
            tx.Start();
            ApplyToTarget(target, scheme, colorFillScheme, filters);
            tx.Commit();
        }

        /// <summary>Apply scheme to multiple views. Manages own transactions.</summary>
        public void ApplySchemeToViews(IEnumerable<View> views, ColorSchemeModel scheme,
            ColorFillScheme? colorFillScheme = null)
        {
            var targets = DeduplicateTargets(views);

            // Tx 1: create filters
            var filters = EnsureFilters(scheme);

            // Tx 2: apply to all targets
            using var tx = new Transaction(_doc, $"DLR — Apply: {scheme.Name}");
            tx.Start();
            foreach (var target in targets)
                ApplyToTarget(target, scheme, colorFillScheme, filters);
            tx.Commit();
        }

        /// <summary>
        /// Generate view templates for each scheme + view type combo.
        /// Three separate transactions: filters → templates → apply filters.
        /// </summary>
        public List<(string Name, ElementId Id)> BatchCreateTemplates(
            IEnumerable<ColorSchemeModel> schemes,
            IEnumerable<ViewTemplateType> viewTypes,
            ColorApplicationMethod method,
            Dictionary<string, ColorFillScheme>? schemeMap = null)
        {
            var created      = new List<(string, ElementId)>();
            var schemesList  = schemes.ToList();
            var viewTypeList = viewTypes.ToList();

            // ── Tx 1: Create all ParameterFilterElements ──────────────────
            var allFilters = new Dictionary<string,
                Dictionary<string, ParameterFilterElement>>(
                StringComparer.OrdinalIgnoreCase);

            using (var tx1 = new Transaction(_doc, "DLR — Create Filters"))
            {
                tx1.Start();
                foreach (var s in schemesList)
                    allFilters[s.Name] = GetOrCreateFiltersForScheme(s);
                tx1.Commit();
            }

            // ── Tx 2: Create view templates ────────────────────────────────
            var templateIds = new Dictionary<string, ElementId>(
                StringComparer.OrdinalIgnoreCase);

            using (var tx2 = new Transaction(_doc, "DLR — Create View Templates"))
            {
                tx2.Start();
                foreach (var scheme in schemesList)
                {
                    foreach (var vt in viewTypeList)
                    {
                        string name = $"{scheme.Name} - {TemplateTypeName(vt)}";

                        // Check if template already exists
                        var existing = new FilteredElementCollector(_doc)
                            .OfClass(typeof(View)).Cast<View>()
                            .FirstOrDefault(v => v.IsTemplate &&
                                string.Equals(v.Name, name,
                                    StringComparison.OrdinalIgnoreCase));

                        if (existing != null)
                        {
                            templateIds[name] = existing.Id;
                            created.Add((name, existing.Id));
                            continue;
                        }

                        try
                        {
                            var tmpl = CreateViewTemplateOfType(vt, name);
                            templateIds[name] = tmpl.Id;
                            created.Add((name, tmpl.Id));
                        }
                        catch (Exception ex)
                        {
                            // Log but continue with other types
                            System.Diagnostics.Debug.WriteLine(
                                $"Could not create template '{name}': {ex.Message}");
                        }
                    }
                }
                tx2.Commit();
            }

            // ── Tx 3: Apply color fill + filters to templates ─────────────
            using (var tx3 = new Transaction(_doc, "DLR — Apply to Templates"))
            {
                tx3.Start();

                foreach (var scheme in schemesList)
                {
                    var filters = allFilters.TryGetValue(scheme.Name, out var f)
                        ? f : new Dictionary<string, ParameterFilterElement>();

                    bool hasSpatial     = scheme.ApplyToRooms || scheme.ApplyToAreas;
                    bool hasFilterCats  = scheme.ApplyToFloors ||
                                         scheme.ApplyToGenericModels ||
                                         scheme.ApplyToMasses;

                    foreach (var vt in viewTypeList)
                    {
                        string name = $"{scheme.Name} - {TemplateTypeName(vt)}";
                        if (!templateIds.TryGetValue(name, out var tmplId)) continue;

                        var view = _doc.GetElement(tmplId) as View;
                        if (view == null) continue;

                        // Color fill for spatial categories
                        if (hasSpatial &&
                            method is ColorApplicationMethod.ColorFillScheme
                                   or ColorApplicationMethod.Both)
                        {
                            if (schemeMap?.TryGetValue(scheme.Name,
                                out var revitScheme) == true && view is ViewPlan vp)
                                TrySetColorSchemeOnView(vp, revitScheme);
                        }

                        // Filters: always for non-spatial; conditional for spatial
                        if (hasFilterCats)
                            ApplyFiltersToView(view, scheme, filters);

                        if (hasSpatial &&
                            method is ColorApplicationMethod.ViewFilters
                                   or ColorApplicationMethod.Both)
                            ApplyFiltersToView(view, scheme, filters);
                    }
                }

                tx3.Commit();
            }

            return created;
        }

        // ── Filter management ──────────────────────────────────────────────

        /// <summary>
        /// Creates ParameterFilterElements for each scheme entry.
        /// MUST be called inside an open transaction.
        /// </summary>
        public Dictionary<string, ParameterFilterElement> GetOrCreateFiltersForScheme(
            ColorSchemeModel scheme)
        {
            var results     = new Dictionary<string, ParameterFilterElement>(
                StringComparer.OrdinalIgnoreCase);
            var categoryIds = BuildCategoryIds(scheme);
            if (!categoryIds.Any()) return results;

            // Type Name for non-spatial; Department for rooms
            var paramId = (scheme.ApplyToRooms || scheme.ApplyToAreas)
                ? new ElementId(BuiltInParameter.ROOM_DEPARTMENT)
                : new ElementId(BuiltInParameter.ALL_MODEL_TYPE_NAME);

            foreach (var entry in scheme.Entries)
            {
                string filterName = $"{scheme.Name} - {entry.Value}";

                var existing = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .FirstOrDefault(f => string.Equals(f.Name, filterName,
                        StringComparison.OrdinalIgnoreCase));

                if (existing != null) { results[entry.Value] = existing; continue; }

                try
                {
                    var rule   = ParameterFilterRuleFactory.CreateEqualsRule(
                                     paramId, entry.Value);
                    var filter = ParameterFilterElement.Create(
                                     _doc, filterName, categoryIds,
                                     new ElementParameterFilter(rule));
                    results[entry.Value] = filter;
                }
                catch { }
            }

            return results;
        }

        /// <summary>
        /// Applies filters to a view with solid-fill color overrides.
        /// MUST be called inside an open transaction.
        /// </summary>
        public void ApplyFiltersToView(View view, ColorSchemeModel scheme,
            Dictionary<string, ParameterFilterElement> filters)
        {
            var solidFillId = GetSolidFillPatternId();

            foreach (var entry in scheme.Entries)
            {
                if (!filters.TryGetValue(entry.Value, out var filter)) continue;
                try
                {
                    if (!view.GetFilters().Contains(filter.Id))
                        view.AddFilter(filter.Id);

                    var color     = new Color(entry.R, entry.G, entry.B);
                    var overrides = new OverrideGraphicSettings();

                    if (solidFillId != ElementId.InvalidElementId)
                    {
                        overrides.SetSurfaceForegroundPatternId(solidFillId);
                        overrides.SetSurfaceForegroundPatternColor(color);
                        overrides.SetSurfaceForegroundPatternVisible(true);
                    }
                    else
                    {
                        overrides.SetSurfaceForegroundPatternColor(color);
                        overrides.SetSurfaceForegroundPatternVisible(true);
                    }

                    view.SetFilterVisibility(filter.Id, true);
                    view.SetFilterOverrides(filter.Id, overrides);
                }
                catch { }
            }
        }

        // ── Color fill scheme ──────────────────────────────────────────────

        public void SetColorFillSchemeOnView(View view, ColorFillScheme scheme)
        {
            if (view is ViewPlan vp) TrySetColorSchemeOnView(vp, scheme);
        }

        private void TrySetColorSchemeOnView(ViewPlan view, ColorFillScheme scheme)
        {
            try
            {
                view.SetColorFillSchemeId(scheme.CategoryId, scheme.Id);
            }
            catch
            {
                try { view.LookupParameter("Color Scheme")?.Set(scheme.Id); } catch { }
            }
        }

        // ── Public helpers ─────────────────────────────────────────────────

        public bool HasSourceViewOfType(ViewTemplateType type)
            => FindSourceView(type) != null;

        public string ViewTypeName(ViewTemplateType type)
            => TemplateTypeName(type);

        // ── Private helpers ────────────────────────────────────────────────

        /// <summary>
        /// Creates filters in their own transaction and returns them.
        /// Safe to call from any context.
        /// </summary>
        private Dictionary<string, ParameterFilterElement> EnsureFilters(
            ColorSchemeModel scheme)
        {
            bool needsFilters = scheme.ApplyToFloors || scheme.ApplyToGenericModels
                                || scheme.ApplyToMasses || scheme.ApplyToRooms
                                || scheme.ApplyToAreas;
            if (!needsFilters)
                return new Dictionary<string, ParameterFilterElement>();

            using var tx = new Transaction(_doc, "DLR — Create Filters");
            tx.Start();
            var filters = GetOrCreateFiltersForScheme(scheme);
            tx.Commit();
            return filters;
        }

        private View GetApplyTarget(View view)
        {
            if (view.ViewTemplateId == ElementId.InvalidElementId) return view;
            return _doc.GetElement(view.ViewTemplateId) as View ?? view;
        }

        private List<View> DeduplicateTargets(IEnumerable<View> views)
        {
            var seen    = new HashSet<ElementId>();
            var targets = new List<View>();

            foreach (var v in views)
            {
                ElementId key;
                View target;

                if (v.ViewTemplateId != ElementId.InvalidElementId)
                {
                    key    = v.ViewTemplateId;
                    target = _doc.GetElement(v.ViewTemplateId) as View ?? v;
                }
                else
                {
                    key    = v.Id;
                    target = v;
                }

                if (seen.Add(key)) targets.Add(target);
            }

            return targets;
        }

        private void ApplyToTarget(View target, ColorSchemeModel scheme,
            ColorFillScheme? colorFillScheme,
            Dictionary<string, ParameterFilterElement> filters)
        {
            if ((scheme.ApplyToRooms || scheme.ApplyToAreas) &&
                colorFillScheme != null && target is ViewPlan vp)
                TrySetColorSchemeOnView(vp, colorFillScheme);

            if ((scheme.ApplyToFloors || scheme.ApplyToGenericModels ||
                 scheme.ApplyToMasses) && filters.Any())
                ApplyFiltersToView(target, scheme, filters);
        }

        private View CreateViewTemplateOfType(ViewTemplateType type, string name)
        {
            var source = FindSourceView(type)
                ?? (type == ViewTemplateType.ThreeD ? CreateDefault3DView() : null);

            if (source == null)
                throw new InvalidOperationException(
                    $"No source view found for {TemplateTypeName(type)}. " +
                    "Create one first.");

            var template = source.CreateViewTemplate();
            template.Name = name;
            return template;
        }

        private View? FindSourceView(ViewTemplateType type) => type switch
        {
            ViewTemplateType.FloorPlan => new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .FirstOrDefault(v => v.ViewType == ViewType.FloorPlan && !v.IsTemplate),

            ViewTemplateType.AreaPlan => new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .FirstOrDefault(v => v.ViewType == ViewType.AreaPlan && !v.IsTemplate),

            ViewTemplateType.ThreeD => new FilteredElementCollector(_doc)
                .OfClass(typeof(View3D)).Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && !v.IsCallout),

            ViewTemplateType.Section => new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSection)).Cast<ViewSection>()
                .FirstOrDefault(v => !v.IsTemplate),

            _ => null
        };

        private View3D CreateDefault3DView()
        {
            try
            {
                var vft = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);

                if (vft == null) return null!;
                var v3d  = View3D.CreateIsometric(_doc, vft.Id);
                v3d.Name = "Color Scheme - Source 3D";
                return v3d;
            }
            catch { return null!; }
        }

        private List<ElementId> BuildCategoryIds(ColorSchemeModel scheme)
        {
            var ids = new List<ElementId>();
            if (scheme.ApplyToRooms)         ids.Add(new ElementId(BuiltInCategory.OST_Rooms));
            if (scheme.ApplyToAreas)         ids.Add(new ElementId(BuiltInCategory.OST_Areas));
            if (scheme.ApplyToFloors)        ids.Add(new ElementId(BuiltInCategory.OST_Floors));
            if (scheme.ApplyToGenericModels) ids.Add(new ElementId(BuiltInCategory.OST_GenericModel));
            if (scheme.ApplyToMasses)        ids.Add(new ElementId(BuiltInCategory.OST_Mass));
            if (!ids.Any()) ids.Add(new ElementId(BuiltInCategory.OST_Rooms));
            return ids;
        }

        private ElementId GetSolidFillPatternId()
        {
            try
            {
                var fp = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
                return fp?.Id ?? ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
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
