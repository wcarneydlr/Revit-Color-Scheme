using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;

namespace ColorSchemeAddin.Services
{
    /// <summary>
    /// Stores addin state flags in Revit ExtensibleStorage on the ProjectInformation element.
    /// This persists per-document — so "has been run" is tracked per RVT file.
    /// </summary>
    public static class ExtensibleStorageService
    {
        private static readonly Guid SchemaGuid = new Guid("A3F7C2D1-84B6-4E91-BC03-5F2A9D8E1047");
        private const string SchemaName       = "DLRColorSchemeAddinState";
        private const string VendorId         = "DLRGroup";
        private const string FieldHasBeenRun  = "HasBeenRun";

        // ── Public API ─────────────────────────────────────────────────────

        /// <summary>Returns true if the addin has been run in this document before.</summary>
        public static bool HasBeenRun(Document doc)
        {
            try
            {
                var schema = GetOrCreateSchema();
                var entity = GetEntity(doc, schema);
                if (entity == null) return false;
                return entity.Get<bool>(schema.GetField(FieldHasBeenRun));
            }
            catch { return false; }
        }

        /// <summary>Marks the addin as having been run in this document.</summary>
        public static void MarkAsRun(Document doc)
        {
            try
            {
                var schema = GetOrCreateSchema();
                var projInfo = doc.ProjectInformation;

                using var tx = new Transaction(doc, "DLR Color Scheme — Mark First Run");
                tx.Start();

                var entity = new Entity(schema);
                entity.Set(schema.GetField(FieldHasBeenRun), true);
                projInfo.SetEntity(entity);

                tx.Commit();
            }
            catch { /* non-fatal — addin still works without this */ }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static Schema GetOrCreateSchema()
        {
            // Try to find an existing schema first
            var existing = Schema.Lookup(SchemaGuid);
            if (existing != null) return existing;

            // Create a new schema
            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetVendorId(VendorId);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldHasBeenRun, typeof(bool));

            return builder.Finish();
        }

        private static Entity? GetEntity(Document doc, Schema schema)
        {
            var projInfo = doc.ProjectInformation;
            var entity = projInfo.GetEntity(schema);
            return entity.IsValid() ? entity : null;
        }
    }
}
