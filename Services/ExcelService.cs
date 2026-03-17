using ClosedXML.Excel;
using ColorSchemeAddin.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ColorSchemeAddin.Services
{
    /// <summary>
    /// Handles import and export of color schemes using the DLR Excel template format.
    /// Template columns: A=Name, B=R, C=G, D=B, E=Preview (filled cell, no value).
    /// One sheet per color scheme; sheet name = scheme name.
    /// </summary>
    public static class ExcelService
    {
        // ── Import ─────────────────────────────────────────────────────────

        /// <summary>
        /// Reads one or more color schemes from a workbook.
        /// Each sheet becomes one ColorSchemeModel. Row 1 = headers, Row 2+ = data.
        /// </summary>
        public static List<ColorSchemeModel> ImportFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Excel file not found: {filePath}");

            var schemes = new List<ColorSchemeModel>();

            using var wb = new XLWorkbook(filePath);
            foreach (IXLWorksheet ws in wb.Worksheets)
            {
                var scheme = new ColorSchemeModel { Name = ws.Name };

                // Find the header row (first non-empty row)
                int headerRow = 1;
                int dataStart = 2;

                // Validate expected columns in header
                string colA = ws.Cell(headerRow, 1).GetString().Trim().ToLower();
                if (colA != "name")
                    throw new InvalidDataException(
                        $"Sheet '{ws.Name}': Expected column A header 'Name', found '{colA}'. " +
                        "Please use the DLR Color Scheme template.");

                int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

                for (int row = dataStart; row <= lastRow; row++)
                {
                    string name = ws.Cell(row, 1).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    if (!TryGetByte(ws.Cell(row, 2), out byte r) ||
                        !TryGetByte(ws.Cell(row, 3), out byte g) ||
                        !TryGetByte(ws.Cell(row, 4), out byte b))
                    {
                        // Skip rows with invalid RGB — could be notes/footers
                        continue;
                    }

                    scheme.Entries.Add(new ColorEntryModel
                    {
                        Value = name,
                        ColorName = name,
                        R = r,
                        G = g,
                        B = b
                    });
                }

                if (scheme.Entries.Count > 0)
                    schemes.Add(scheme);
            }

            if (schemes.Count == 0)
                throw new InvalidDataException("No valid color scheme data found in the workbook.");

            return schemes;
        }

        // ── Export ─────────────────────────────────────────────────────────

        /// <summary>
        /// Exports one or more color schemes to an Excel workbook.
        /// Each scheme becomes a sheet; format matches the DLR import template.
        /// </summary>
        public static void ExportToFile(IEnumerable<ColorSchemeModel> schemes, string filePath)
        {
            using var wb = new XLWorkbook();

            foreach (var scheme in schemes)
            {
                // Sanitize sheet name (Excel limit: 31 chars, no special chars)
                string sheetName = SanitizeSheetName(scheme.Name);
                var ws = wb.Worksheets.Add(sheetName);

                WriteHeader(ws);

                int row = 2;
                foreach (var entry in scheme.Entries)
                {
                    ws.Cell(row, 1).Value = entry.Value;
                    ws.Cell(row, 2).Value = entry.R;
                    ws.Cell(row, 3).Value = entry.G;
                    ws.Cell(row, 4).Value = entry.B;

                    // Preview cell: filled with the entry's color
                    var previewCell = ws.Cell(row, 5);
                    previewCell.Style.Fill.BackgroundColor = XLColor.FromArgb(255, entry.R, entry.G, entry.B);

                    row++;
                }

                AutoFitColumns(ws);
            }

            wb.SaveAs(filePath);
        }

        /// <summary>
        /// Generates a blank template workbook with one example sheet.
        /// </summary>
        public static void ExportTemplate(string filePath)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("My Color Scheme");

            WriteHeader(ws);

            // Example row
            ws.Cell(2, 1).Value = "Room";
            ws.Cell(2, 2).Value = 100;
            ws.Cell(2, 3).Value = 100;
            ws.Cell(2, 4).Value = 100;
            ws.Cell(2, 5).Style.Fill.BackgroundColor = XLColor.FromArgb(255, 100, 100, 100);

            // Instruction note in row 4
            ws.Cell(4, 1).Value = "↑ Replace the example row above. Add as many rows as needed.";
            ws.Cell(4, 1).Style.Font.Italic = true;
            ws.Cell(4, 1).Style.Font.FontColor = XLColor.FromArgb(255, 89, 89, 85);
            ws.Range(4, 1, 4, 5).Merge();

            // Rename sheet tip
            ws.Cell(5, 1).Value = "Rename this sheet to name your color scheme. Add more sheets for more schemes.";
            ws.Cell(5, 1).Style.Font.Italic = true;
            ws.Cell(5, 1).Style.Font.FontColor = XLColor.FromArgb(255, 89, 89, 85);
            ws.Range(5, 1, 5, 5).Merge();

            AutoFitColumns(ws);
            wb.SaveAs(filePath);
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static void WriteHeader(IXLWorksheet ws)
        {
            var headers = new[] { "Name", "R", "G", "B", "Preview" };
            for (int col = 1; col <= headers.Length; col++)
            {
                var cell = ws.Cell(1, col);
                cell.Value = headers[col - 1];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(255, 39, 49, 63);   // #27313F
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.BottomBorderColor = XLColor.FromArgb(255, 219, 213, 205); // #DBD5CD
            }
        }

        private static void AutoFitColumns(IXLWorksheet ws)
        {
            ws.Column(1).Width = 28;   // Name — wider
            ws.Column(2).Width = 8;    // R
            ws.Column(3).Width = 8;    // G
            ws.Column(4).Width = 8;    // B
            ws.Column(5).Width = 14;   // Preview
        }

        private static bool TryGetByte(IXLCell cell, out byte value)
        {
            value = 0;
            if (cell.IsEmpty()) return false;
            if (!cell.TryGetValue(out int intVal)) return false;
            if (intVal < 0 || intVal > 255) return false;
            value = (byte)intVal;
            return true;
        }

        private static string SanitizeSheetName(string name)
        {
            var invalid = new[] { '/', '\\', '?', '*', '[', ']', ':' };
            foreach (char c in invalid)
                name = name.Replace(c, '_');
            return name.Length > 31 ? name[..31] : name;
        }
    }
}
