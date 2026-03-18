using Autodesk.Revit.DB;
using ColorSchemeAddin.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorSchemeAddin.Services
{
    /// <summary>
    /// Creates and updates Revit Materials for each color scheme entry.
    /// Naming convention: "[SchemeName] - [EntryValue]"
    /// Duplicates from "Color Scheme" template material if present.
    ///
    /// Revit 2024 API note:
    ///   Material.Duplicate(string) returns Material (not ElementId).
    ///   Material.Create(Document, string) returns ElementId.
    /// </summary>
    public class MaterialService
    {
        private const string TemplateMaterialName = "Color Scheme";
        private readonly Document _doc;

        public MaterialService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public Dictionary<string, ElementId> SyncMaterials(ColorSchemeModel scheme)
        {
            var results = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            Material? templateMaterial = FindMaterialByName(TemplateMaterialName);

            using var tx = new Transaction(_doc, $"Sync Color Scheme Materials: {scheme.Name}");
            tx.Start();

            foreach (var entry in scheme.Entries)
            {
                string matName = $"{scheme.Name} - {entry.Value}";
                Material? existing = FindMaterialByName(matName);

                if (existing != null)
                {
                    SetMaterialColor(existing, entry.R, entry.G, entry.B);
                    results[entry.Value] = existing.Id;
                }
                else
                {
                    Material newMat;

                    if (templateMaterial != null)
                    {
                        // Material.Duplicate returns Material directly in Revit 2024
                        newMat = templateMaterial.Duplicate(matName);
                    }
                    else
                    {
                        // Material.Create returns ElementId — fetch element afterwards
                        ElementId newId = Material.Create(_doc, matName);
                        newMat = (Material)_doc.GetElement(newId);
                        newMat.SurfaceForegroundPatternId = ElementId.InvalidElementId;
                        newMat.SurfaceBackgroundPatternId = ElementId.InvalidElementId;
                        newMat.CutForegroundPatternId     = ElementId.InvalidElementId;
                        newMat.CutBackgroundPatternId     = ElementId.InvalidElementId;
                    }

                    SetMaterialColor(newMat, entry.R, entry.G, entry.B);
                    results[entry.Value] = newMat.Id;
                }
            }

            tx.Commit();
            return results;
        }

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

        private Material? FindMaterialByName(string name)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m =>
                    string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
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
