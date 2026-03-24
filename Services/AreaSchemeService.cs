using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorSchemeAddin.Services
{
    /// <summary>
    /// Manages Revit AreaScheme elements.
    /// Duplicates from "Gross Building" if "Color Schemes" doesn't exist.
    /// </summary>
    public class AreaSchemeService
    {
        private const string ColorSchemesAreaSchemeName = "Color Schemes";
        private readonly Document _doc;

        public AreaSchemeService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        /// <summary>Returns all AreaScheme elements in the document.</summary>
        public List<AreaScheme> GetAllAreaSchemes()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(AreaScheme))
                .Cast<AreaScheme>()
                .OrderBy(a => a.Name)
                .ToList();
        }

        /// <summary>
        /// Finds or creates an AreaScheme named "Color Schemes".
        /// Duplicates from "Gross Building" if not found.
        /// </summary>
        public AreaScheme GetOrCreateColorSchemesAreaScheme()
        {
            // Check if it already exists
            var existing = GetAllAreaSchemes()
                .FirstOrDefault(a => string.Equals(a.Name,
                    ColorSchemesAreaSchemeName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            // Find a base to duplicate from — prefer "Gross Building"
            var schemes = GetAllAreaSchemes();
            var baseScheme = schemes.FirstOrDefault(a =>
                a.Name.IndexOf("Gross", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? schemes.FirstOrDefault();

            if (baseScheme == null)
                throw new InvalidOperationException(
                    "No existing Area Schemes found in the document to duplicate from. " +
                    "Please create at least one Area Plan in Revit first.");

            using var tx = new Transaction(_doc, "Create 'Color Schemes' Area Scheme");
            tx.Start();

            var newIds = ElementTransformUtils.CopyElement(_doc, baseScheme.Id, XYZ.Zero);
            var newScheme = (AreaScheme)_doc.GetElement(newIds.First());
            newScheme.Name = ColorSchemesAreaSchemeName;

            tx.Commit();
            return newScheme;
        }
    }
}
