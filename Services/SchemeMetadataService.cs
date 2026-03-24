using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorSchemeAddin.Services
{
    /// <summary>
    /// Stores DLR Color Scheme metadata in Extensible Storage on each
    /// ColorFillScheme element. Persists: applied categories, parameter name.
    /// </summary>
    public static class SchemeMetadataService
    {
        private static readonly Guid SchemaGuid =
            new Guid("B7F3A2C1-94D5-4E82-AD01-6C3F8E7B2094");
        private const string SchemaName       = "DLRColorSchemeMetadata";
        private const string VendorId         = "DLRGroup";
        private const string FieldCategories  = "AppliedCategories";  // comma-separated BuiltInCategory ints
        private const string FieldParameter   = "ParameterName";

        // ── Public API ─────────────────────────────────────────────────────

        public static void SaveMetadata(
            Document doc,
            ColorFillScheme scheme,
            IEnumerable<BuiltInCategory> categories,
            string parameterName)
        {
            try
            {
                var schema = GetOrCreateSchema();
                using var tx = new Transaction(doc, "DLR — Save Scheme Metadata");
                tx.Start();

                var entity = new Entity(schema);

                // Store categories as comma-separated int values
                var catString = string.Join(",",
                    categories.Select(c => ((int)c).ToString()));
                entity.Set(schema.GetField(FieldCategories), catString);
                entity.Set(schema.GetField(FieldParameter),  parameterName ?? string.Empty);

                scheme.SetEntity(entity);
                tx.Commit();
            }
            catch { /* non-fatal */ }
        }

        public static (List<BuiltInCategory> categories, string parameterName)
            LoadMetadata(ColorFillScheme scheme)
        {
            var defaultCats = new List<BuiltInCategory>();
            var defaultParam = "Department";

            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return (defaultCats, defaultParam);

                var entity = scheme.GetEntity(schema);
                if (!entity.IsValid()) return (defaultCats, defaultParam);

                // Read parameter name
                var paramName = entity.Get<string>(schema.GetField(FieldParameter));

                // Read categories
                var catString = entity.Get<string>(schema.GetField(FieldCategories));
                var cats = new List<BuiltInCategory>();
                if (!string.IsNullOrEmpty(catString))
                {
                    foreach (var part in catString.Split(','))
                    {
                        if (int.TryParse(part.Trim(), out int val))
                            cats.Add((BuiltInCategory)val);
                    }
                }

                return (cats, string.IsNullOrEmpty(paramName) ? defaultParam : paramName);
            }
            catch { return (defaultCats, defaultParam); }
        }

        public static bool HasMetadata(ColorFillScheme scheme)
        {
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return false;
                var entity = scheme.GetEntity(schema);
                return entity.IsValid();
            }
            catch { return false; }
        }

        // ── Schema ─────────────────────────────────────────────────────────

        private static Schema GetOrCreateSchema()
        {
            var existing = Schema.Lookup(SchemaGuid);
            if (existing != null) return existing;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetVendorId(VendorId);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldCategories, typeof(string));
            builder.AddSimpleField(FieldParameter,  typeof(string));

            return builder.Finish();
        }
    }
}
