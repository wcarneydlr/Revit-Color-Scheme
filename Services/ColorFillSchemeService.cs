using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using ColorSchemeAddin.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorSchemeAddin.Services
{
    /// <summary>
    /// Creates, reads, and updates Revit ColorFillScheme elements.
    /// Revit 2024 API: ColorFillScheme uses AddEntry/GetEntries, not Create/SetEntry.
    /// </summary>
    public class ColorFillSchemeService
    {
        private readonly Document _doc;

        public ColorFillSchemeService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        // ── Read ───────────────────────────────────────────────────────────

        public List<ColorFillScheme> GetAllSchemes()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(ColorFillScheme))
                .Cast<ColorFillScheme>()
                .OrderBy(s => s.Name)
                .ToList();
        }

        public ColorFillScheme? FindByName(string name)
        {
            return GetAllSchemes().FirstOrDefault(s =>
                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public ColorSchemeModel ToModel(ColorFillScheme scheme)
        {
            // Load metadata from extensible storage (saved by addin on create/edit)
            var (storedCats, storedParam) = SchemeMetadataService.LoadMetadata(scheme);

            var model = new ColorSchemeModel
            {
                Name          = scheme.Name,
                ParameterName = storedParam,
                // Seed from extensible storage if available, else fall back to CategoryId
                ApplyToRooms  = storedCats.Count > 0
                    ? storedCats.Contains(BuiltInCategory.OST_Rooms)
                    : scheme.CategoryId == new ElementId(BuiltInCategory.OST_Rooms),
                ApplyToAreas  = storedCats.Count > 0
                    ? storedCats.Contains(BuiltInCategory.OST_Areas)
                    : scheme.CategoryId == new ElementId(BuiltInCategory.OST_Areas),
                ApplyToFloors        = storedCats.Contains(BuiltInCategory.OST_Floors),
                ApplyToGenericModels = storedCats.Contains(BuiltInCategory.OST_GenericModel),
                ApplyToMasses        = storedCats.Contains(BuiltInCategory.OST_Mass),
            };
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

        public ColorFillScheme CreateScheme(ColorSchemeModel model, ElementId? categoryId = null)
        {
            if (string.IsNullOrWhiteSpace(model.Name))
                throw new ArgumentException("Scheme name cannot be empty.");
            if (model.Entries.Count == 0)
                throw new ArgumentException("Scheme must have at least one color entry.");

            var catId = categoryId ?? new ElementId(BuiltInCategory.OST_Rooms);

            using var tx = new Transaction(_doc, $"Create Color Scheme: {model.Name}");
            tx.Start();

            // Revit 2024: ColorFillScheme.Create does not exist.
            // We must duplicate an existing scheme or use the FillPatternElement approach.
            // The supported way is to get an existing scheme and copy it, or use
            // the document's built-in scheme and rename via parameter.
            // Best practice: find any existing scheme to duplicate, or create via
            // the BuiltInParameter approach on a view.
            //
            // Revit 2024 actual API: new ColorFillScheme is created by
            // duplicating an existing one then modifying entries.
            var existingSchemes = GetAllSchemes();
            ColorFillScheme scheme;

            if (existingSchemes.Count > 0)
            {
                // Duplicate the first available scheme as a base
                ElementId newId = existingSchemes[0].Duplicate(model.Name);
                scheme = (ColorFillScheme)_doc.GetElement(newId);

                // Clear existing entries by replacing with ours
                ClearAndPopulate(scheme, model);
            }
            else
            {
                // No existing scheme to duplicate — notify caller
                tx.RollBack();
                throw new InvalidOperationException(
                    "No existing Color Fill Scheme found in the document to use as a base. " +
                    "Please create at least one Color Fill Scheme manually in Revit first " +
                    "(Architecture tab → Room & Area → Color Schemes), then try again.");
            }

            tx.Commit();
            return scheme;
        }

        // ── Update ─────────────────────────────────────────────────────────

        public void UpdateScheme(ColorFillScheme scheme, ColorSchemeModel model)
        {
            using var tx = new Transaction(_doc, $"Update Color Scheme: {scheme.Name}");
            tx.Start();
            ClearAndPopulate(scheme, model);
            tx.Commit();
        }

        // ── Apply to Views ─────────────────────────────────────────────────

        public void ApplySchemeToFloorPlans(ColorFillScheme scheme)
        {
            var views = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => v.ViewType == ViewType.FloorPlan && !v.IsTemplate)
                .ToList();

            if (views.Count == 0) return;

            using var tx = new Transaction(_doc, $"Apply Color Scheme: {scheme.Name}");
            tx.Start();
            foreach (var view in views)
                TryApplySchemeToView(view, scheme);
            tx.Commit();
        }

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
                TryApplySchemeToView(view, scheme);
            tx.Commit();
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private void TryApplySchemeToView(ViewPlan view, ColorFillScheme scheme)
        {
            try
            {
                // Revit 2024: color fill scheme is set via the
                // ROOM_COLOR_SCHEME_ID / AREA_COLOR_SCHEME_ID built-in parameter on the view
                // Look up the color scheme parameter by name since the
                // BuiltInParameter enum name varies by Revit version
                Parameter? param = view.LookupParameter("Color Scheme");

                param?.Set(scheme.Id);
            }
            catch { /* skip views where scheme can't be applied */ }
        }

        private void ClearAndPopulate(ColorFillScheme scheme, ColorSchemeModel model)
        {
            // Remove all existing entries
            var existing = scheme.GetEntries().ToList();
            foreach (var entry in existing)
            {
                try { scheme.RemoveEntry(entry); } catch { }
            }

            // Add entries from model
            foreach (var modelEntry in model.Entries)
            {
                try
                {
                    var entry = new ColorFillSchemeEntry(StorageType.String);
                    entry.SetStringValue(modelEntry.Value);
                    entry.Color = new Color(modelEntry.R, modelEntry.G, modelEntry.B);
                    scheme.AddEntry(entry);
                    modelEntry.IsMapped = true;
                }
                catch
                {
                    modelEntry.IsMapped = false;
                }
            }
        }

        /// <summary>
        /// Creates or updates a ColorFillScheme for OST_Areas linked to the given
        /// AreaScheme, populating it with entries from the model.
        /// This makes the color scheme appear in Revit's area plan color fill dialog.
        /// </summary>
        public void EnsureAreaColorFillScheme(ColorSchemeModel model, AreaScheme areaScheme)
        {
            // Look for existing scheme: same name, OST_Areas, same AreaSchemeId
            var areaSchemeId    = new ElementId(BuiltInCategory.OST_Areas);
            var existingSchemes = new FilteredElementCollector(_doc)
                .OfClass(typeof(ColorFillScheme))
                .Cast<ColorFillScheme>()
                .Where(s => s.CategoryId == areaSchemeId &&
                            s.AreaSchemeId == areaScheme.Id)
                .ToList();

            // Find one with matching name or use the first one
            var existing = existingSchemes
                .FirstOrDefault(s => string.Equals(s.Name, model.Name,
                    StringComparison.OrdinalIgnoreCase))
                ?? existingSchemes.FirstOrDefault();

            using var tx = new Transaction(_doc,
                $"DLR — Create Area Color Fill Scheme: {model.Name}");
            tx.Start();

            ColorFillScheme targetScheme;

            if (existing != null)
            {
                targetScheme = existing;
                // Rename if needed
                if (!string.Equals(existing.Name, model.Name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    try { existing.Name = model.Name; } catch { }
                }
            }
            else
            {
                // Duplicate from the first available scheme for this area scheme
                // (Revit requires duplication — cannot create from scratch)
                if (existingSchemes.Count == 0)
                {
                    // Find any OST_Areas scheme to duplicate from
                    var anyAreaScheme = new FilteredElementCollector(_doc)
                        .OfClass(typeof(ColorFillScheme))
                        .Cast<ColorFillScheme>()
                        .FirstOrDefault(s => s.CategoryId == areaSchemeId);

                    if (anyAreaScheme == null)
                    {
                        tx.RollBack();
                        return; // No source to duplicate from
                    }

                    var newId = anyAreaScheme.Duplicate(model.Name);
                    targetScheme = (ColorFillScheme)_doc.GetElement(newId);
                }
                else
                {
                    var newId = existingSchemes.First().Duplicate(model.Name);
                    targetScheme = (ColorFillScheme)_doc.GetElement(newId);
                }
            }

            // Clear existing entries and repopulate from model
            try
            {
                var entries = targetScheme.GetEntries().ToList();
                foreach (var entry in entries)
                {
                    try { targetScheme.RemoveEntry(entry); } catch { }
                }
            }
            catch { }

            // Add entries from model
            foreach (var modelEntry in model.Entries)
            {
                try
                {
                    var entry = new ColorFillSchemeEntry(StorageType.String);
                    entry.SetStringValue(modelEntry.Value);
                    entry.Color = new Color(modelEntry.R, modelEntry.G, modelEntry.B);
                    entry.Caption = modelEntry.Value;
                    entry.FillPatternId = GetSolidFillPatternId();
                    targetScheme.AddEntry(entry);
                }
                catch { }
            }

            tx.Commit();
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
    }
}
