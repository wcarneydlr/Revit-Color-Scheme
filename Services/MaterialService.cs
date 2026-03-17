using Autodesk.Revit.DB;
using ColorSchemeAddin.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorSchemeAddin.Services
{
    /// <summary>
    /// Creates and updates Revit Materials for each color scheme entry.
    /// Duplicates from a template material named "Color Scheme" if present;
    /// otherwise creates a new generic material.
    /// Naming convention: "[SchemeName] - [EntryValue]"
    /// </summary>
    public class MaterialService
    {
        private const string TemplateMaterialName = "Color Scheme";

        private readonly Document _doc;

        public MaterialService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        // ── Public API ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates or updates materials for every entry in the scheme.
        /// Returns a dictionary: entry Value → Material ElementId.
        /// </summary>
        public Dictionary<string, ElementId> SyncMaterials(ColorSchemeModel scheme)
        {
            var results = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            var templateMaterial = FindTemplateMaterial();

            using var tx = new Transaction(_doc, $"Sync Color Scheme Materials: {scheme.Name}");
            tx.Start();

            foreach (var entry in scheme.Entries)
            {
                string matName = MaterialName(scheme.Name, entry.Value);
                var existing = FindMaterialByName(matName);

                ElementId matId;
                if (existing != null)
                {
                    // Update color of existing material
                    SetMaterialColor(existing, entry.R, entry.G, entry.B);
                    matId = existing.Id;
                }
                else
                {
                    // Duplicate template or create new
                    var newMat = templateMaterial != null
                        ? DuplicateMaterial(templateMaterial, matName)
                        : CreateNewMaterial(matName);

                    SetMaterialColor(newMat, entry.R, entry.G, entry.B);
                    matId = newMat.Id;
                }

                results[entry.Value] = matId;
            }

            tx.Commit();
            return results;
        }

        /// <summary>Returns all materials whose names start with "[schemeName] - ".</summary>
        public List<Material> GetMaterialsForScheme(string schemeName)
        {
            string prefix = $"{schemeName} - ";
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .Where(m => m.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.Name)
                .ToList();
        }

        // ── Private helpers ────────────────────────────────────────────────

        private static string MaterialName(string schemeName, string entryValue)
            => $"{schemeName} - {entryValue}";

        private Material? FindTemplateMaterial()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m =>
                    string.Equals(m.Name, TemplateMaterialName, StringComparison.OrdinalIgnoreCase));
        }

        private Material? FindMaterialByName(string name)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m =>
                    string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private Material DuplicateMaterial(Material source, string newName)
        {
            ElementId newId = source.Duplicate(newName);
            return (Material)_doc.GetElement(newId);
        }

        private Material CreateNewMaterial(string name)
        {
            ElementId matId = Material.Create(_doc, name);
            var mat = (Material)_doc.GetElement(matId);

            // Set a generic appearance
            mat.SurfaceForegroundPatternId = ElementId.InvalidElementId;
            mat.SurfaceBackgroundPatternId = ElementId.InvalidElementId;
            mat.CutForegroundPatternId     = ElementId.InvalidElementId;
            mat.CutBackgroundPatternId     = ElementId.InvalidElementId;

            return mat;
        }

        private void SetMaterialColor(Material mat, byte r, byte g, byte b)
        {
            var color = new Color(r, g, b);
            mat.Color = color;
            mat.SurfaceForegroundPatternColor = color;
            mat.CutForegroundPatternColor     = color;
        }
    }
}
