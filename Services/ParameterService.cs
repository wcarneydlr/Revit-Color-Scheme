using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorSchemeAddin.Services
{
    /// <summary>
    /// Creates Text project parameters bound to specified categories.
    /// Uses a temporary shared parameter file — no permanent SPF required.
    /// Revit 2024 compatible.
    /// </summary>
    public class ParameterService
    {
        private readonly Document _doc;

        public ParameterService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        /// <summary>
        /// Returns parameter names available for color fill schemes —
        /// built-in defaults plus any project Text parameters on relevant categories.
        /// </summary>
        public List<string> GetTextParameterNames(IEnumerable<BuiltInCategory> categories)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Department", "Name", "Occupancy", "Space Type",
                "Phase", "Level", "Function", "Comments"
            };

            try
            {
                // Add project parameters that are Text type and bound to relevant cats
                var catNames = new HashSet<string>(
                    categories
                        .Select(c => { try { return Category.GetCategory(_doc, c)?.Name; } catch { return null; } })
                        .Where(n => n != null)!,
                    StringComparer.OrdinalIgnoreCase);

                foreach (System.Collections.DictionaryEntry entry in _doc.ParameterBindings)
                {
                    if (entry.Key is Definition def &&
                        entry.Value is InstanceBinding instBinding)
                    {
                        // Check if any bound category matches our target categories
                        foreach (Category cat in instBinding.Categories)
                        {
                            if (catNames.Contains(cat.Name))
                            {
                                names.Add(def.Name);
                                break;
                            }
                        }
                    }
                }
            }
            catch { /* non-fatal — return defaults */ }

            return names.OrderBy(n => n).ToList();
        }

        /// <summary>
        /// Creates a Text project parameter bound to the given categories.
        /// Uses a temporary shared parameter file written to %TEMP%.
        /// Returns the parameter name (may already exist — that's fine).
        /// </summary>
        public string CreateProjectParameter(
            string parameterName,
            IEnumerable<BuiltInCategory> boundCategories)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
                throw new ArgumentException("Parameter name cannot be empty.");

            // Return early if already exists
            foreach (System.Collections.DictionaryEntry entry in _doc.ParameterBindings)
            {
                if (entry.Key is Definition def &&
                    string.Equals(def.Name, parameterName.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    return parameterName.Trim();
            }

            string cleanName = parameterName.Trim();
            string tempFile  = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"DLRColorScheme_{Guid.NewGuid():N}.txt");

            var app = _doc.Application;
            string originalSpFile = string.Empty;

            try
            {
                // Save original shared param file path
                try { originalSpFile = app.SharedParametersFilename ?? string.Empty; }
                catch { }

                // Write minimal shared param file
                System.IO.File.WriteAllText(tempFile,
                    "# This is a Revit shared parameter file.\r\n" +
                    "# Do not edit manually!\r\n" +
                    "*META\tVERSION\tMINVERSION\r\n" +
                    "META\t2\t1\r\n" +
                    "*GROUP\tID\tNAME\r\n" +
                    "GROUP\t1\tDLR Color Scheme Parameters\r\n" +
                    "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\t" +
                    "VISIBILITY\tUSERMODIFIABLE\tDESCRIPTION\tUSERVISIBLE\r\n" +
                    $"PARAM\t{Guid.NewGuid()}\t{cleanName}\tTEXT\t\t1\t1\t1\t\t1\r\n");

                app.SharedParametersFilename = tempFile;
                var spFile = app.OpenSharedParameterFile();

                if (spFile == null)
                    throw new InvalidOperationException(
                        "Could not open temporary shared parameter file.");

                var group  = spFile.Groups.get_Item("DLR Color Scheme Parameters");
                var extDef = group?.Definitions.get_Item(cleanName) as ExternalDefinition;

                if (extDef == null)
                    throw new InvalidOperationException(
                        $"Could not read parameter '{cleanName}' from temp file.");

                // Build category set
                var catSet = new CategorySet();
                foreach (var bic in boundCategories)
                {
                    try
                    {
                        var cat = Category.GetCategory(_doc, bic);
                        if (cat != null && cat.AllowsBoundParameters)
                            catSet.Insert(cat);
                    }
                    catch { }
                }

                if (catSet.IsEmpty)
                    throw new InvalidOperationException(
                        "None of the selected categories allow bound parameters. " +
                        "Try selecting Rooms or Areas.");

                using var tx = new Transaction(_doc,
                    $"Create Project Parameter: {cleanName}");
                tx.Start();

                var binding = new InstanceBinding(catSet);

                // Revit 2024: use ForgeTypeId overload instead of deprecated BuiltInParameterGroup
                bool ok = _doc.ParameterBindings.Insert(
                    extDef, binding, GroupTypeId.IdentityData);

                if (!ok)
                    throw new InvalidOperationException(
                        $"Could not bind parameter '{cleanName}'. " +
                        "It may already exist with an incompatible type.");

                tx.Commit();
                return cleanName;
            }
            finally
            {
                // Restore original shared param file
                try
                {
                    app.SharedParametersFilename = originalSpFile;
                }
                catch { }

                // Delete temp file
                try { System.IO.File.Delete(tempFile); } catch { }
            }
        }
    }
}
