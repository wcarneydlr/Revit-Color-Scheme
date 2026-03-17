using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using ColorSchemeAddin.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorSchemeAddin.Services
{
    /// <summary>
    /// Creates, reads, and updates Revit ColorFillScheme elements from ColorSchemeModel data.
    /// </summary>
    public class ColorFillSchemeService
    {
        private readonly Document _doc;

        public ColorFillSchemeService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        // ── Read ───────────────────────────────────────────────────────────

        /// <summary>Returns all ColorFillScheme elements in the document.</summary>
        public List<ColorFillScheme> GetAllSchemes()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(ColorFillScheme))
                .Cast<ColorFillScheme>()
                .OrderBy(s => s.Name)
                .ToList();
        }

        /// <summary>Finds a ColorFillScheme by name, or null if not found.</summary>
        public ColorFillScheme? FindByName(string name)
        {
            return GetAllSchemes().FirstOrDefault(s =>
                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Converts a Revit ColorFillScheme to our model for display/export.
        /// </summary>
        public ColorSchemeModel ToModel(ColorFillScheme scheme)
        {
            var model = new ColorSchemeModel { Name = scheme.Name };

            foreach (ColorFillSchemeEntry entry in scheme.GetEntries())
            {
                model.Entries.Add(new ColorEntryModel
                {
                    Value     = entry.GetStringValue(),
                    ColorName = entry.GetStringValue(),
                    R         = entry.Color.Red,
                    G         = entry.Color.Green,
                    B         = entry.Color.Blue,
                    IsMapped  = true
                });
            }

            return model;
        }

        // ── Create ─────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new ColorFillScheme from a model.
        /// Targets Rooms by default; pass a different CategoryId to target areas, etc.
        /// </summary>
        public ColorFillScheme CreateScheme(ColorSchemeModel model, ElementId? categoryId = null)
        {
            if (string.IsNullOrWhiteSpace(model.Name))
                throw new ArgumentException("Scheme name cannot be empty.");
            if (model.Entries.Count == 0)
                throw new ArgumentException("Scheme must have at least one color entry.");

            // Default: rooms
            var catId = categoryId ?? new ElementId(BuiltInCategory.OST_Rooms);

            using var tx = new Transaction(_doc, $"Create Color Scheme: {model.Name}");
            tx.Start();

            var scheme = ColorFillScheme.Create(_doc, catId, model.Name);
            scheme = PopulateScheme(scheme, model);

            tx.Commit();
            return scheme;
        }

        // ── Update ─────────────────────────────────────────────────────────

        /// <summary>
        /// Updates an existing ColorFillScheme's entries to match the model.
        /// Adds new entries, updates colors on existing ones, removes entries not in model.
        /// </summary>
        public void UpdateScheme(ColorFillScheme scheme, ColorSchemeModel model)
        {
            using var tx = new Transaction(_doc, $"Update Color Scheme: {scheme.Name}");
            tx.Start();

            // Build lookup of model entries by value
            var modelLookup = model.Entries.ToDictionary(
                e => e.Value, e => e, StringComparer.OrdinalIgnoreCase);

            // Existing entries: update color where value matches
            var existingEntries = scheme.GetEntries().ToList();
            foreach (var entry in existingEntries)
            {
                string val = entry.GetStringValue();
                if (modelLookup.TryGetValue(val, out var modelEntry))
                {
                    entry.Color = new Color(modelEntry.R, modelEntry.G, modelEntry.B);
                }
            }

            // Add entries that don't exist yet
            var existingValues = existingEntries
                .Select(e => e.GetStringValue())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var modelEntry in model.Entries)
            {
                if (!existingValues.Contains(modelEntry.Value))
                {
                    var newEntry = ColorFillSchemeEntry.CreateSchemeEntry(
                        _doc,
                        new Color(modelEntry.R, modelEntry.G, modelEntry.B));
                    newEntry.SetStringValue(modelEntry.Value);
                    scheme.SetEntry(newEntry);
                }
            }

            tx.Commit();
        }

        // ── Apply to Views ─────────────────────────────────────────────────

        /// <summary>
        /// Applies a ColorFillScheme to all floor plan views in the document.
        /// </summary>
        public void ApplySchemeToFloorPlans(ColorFillScheme scheme)
        {
            var views = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => v.ViewType == ViewType.FloorPlan && !v.IsTemplate)
                .ToList();

            if (views.Count == 0) return;

            using var tx = new Transaction(_doc, $"Apply Color Scheme to Floor Plans: {scheme.Name}");
            tx.Start();
            foreach (var view in views)
            {
                try
                {
                    // SpatialElementColorFillType drives room color fills
                    var colorFillType = new FilteredElementCollector(_doc, view.Id)
                        .OfClass(typeof(SpatialElementColorFillType))
                        .Cast<SpatialElementColorFillType>()
                        .FirstOrDefault();

                    if (colorFillType != null)
                        colorFillType.ColorFillSchemeId = scheme.Id;
                }
                catch { /* skip views where scheme can't be applied */ }
            }
            tx.Commit();
        }

        /// <summary>
        /// Applies a ColorFillScheme to all area plan views.
        /// </summary>
        public void ApplySchemeToAreaPlans(ColorFillScheme scheme)
        {
            var views = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => v.ViewType == ViewType.AreaPlan && !v.IsTemplate)
                .ToList();

            if (views.Count == 0) return;

            using var tx = new Transaction(_doc, $"Apply Color Scheme to Area Plans: {scheme.Name}");
            tx.Start();
            foreach (var view in views)
            {
                try
                {
                    var colorFillType = new FilteredElementCollector(_doc, view.Id)
                        .OfClass(typeof(SpatialElementColorFillType))
                        .Cast<SpatialElementColorFillType>()
                        .FirstOrDefault();

                    if (colorFillType != null)
                        colorFillType.ColorFillSchemeId = scheme.Id;
                }
                catch { }
            }
            tx.Commit();
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private ColorFillScheme PopulateScheme(ColorFillScheme scheme, ColorSchemeModel model)
        {
            foreach (var entry in model.Entries)
            {
                try
                {
                    var schemeEntry = ColorFillSchemeEntry.CreateSchemeEntry(
                        _doc,
                        new Color(entry.R, entry.G, entry.B));
                    schemeEntry.SetStringValue(entry.Value);
                    scheme.SetEntry(schemeEntry);
                    entry.IsMapped = true;
                }
                catch
                {
                    entry.IsMapped = false;
                }
            }
            return scheme;
        }
    }
}
