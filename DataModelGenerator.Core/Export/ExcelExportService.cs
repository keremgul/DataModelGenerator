using ClosedXML.Excel;
using DataModelGenerator.Core.Simulation;

namespace DataModelGenerator.Core.Export;

/// <summary>Örnek veri simülasyonunu, her varlık için ayrı bir sekme olacak şekilde .xlsx dosyasına yazar.</summary>
public class ExcelExportService
{
    public void Export(SampleDataSet dataSet, string outputPath)
    {
        using var workbook = new XLWorkbook();
        var usedSheetNames = new List<string>();

        foreach (var table in dataSet.Tables)
        {
            var worksheet = workbook.AddWorksheet(UniqueSheetName(table.TechnicalName, usedSheetNames));

            for (var c = 0; c < table.Columns.Count; c++)
            {
                var column = table.Columns[c];
                var cell = worksheet.Cell(1, c + 1);

                cell.Value = column.TechnicalName;
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(
                    column.IsPrimaryKey ? "#FCE4D6" : column.IsForeignKey ? "#DDEBF7" : "#D9E1F2");
                cell.Style.Alignment.WrapText = true;

                var keyNote = column.IsPrimaryKey ? "\nBirincil anahtar" : column.IsForeignKey
                    ? $"\nYabancı anahtar → {column.ReferencesEntity}.{column.ReferencesAttribute}"
                    : string.Empty;
                cell.GetComment().AddText($"{column.Name}{keyNote}");
            }

            for (var r = 0; r < table.Rows.Count; r++)
            {
                for (var c = 0; c < table.Columns.Count; c++)
                {
                    var cell = worksheet.Cell(r + 2, c + 1);
                    switch (table.Rows[r][c])
                    {
                        case null:
                            break;
                        case DateTime dateTime:
                            cell.Value = dateTime;
                            cell.Style.DateFormat.Format = dateTime.TimeOfDay == TimeSpan.Zero
                                ? "dd.MM.yyyy"
                                : "dd.MM.yyyy HH:mm";
                            break;
                        case bool flag:
                            cell.Value = flag;
                            break;
                        case int number:
                            cell.Value = number;
                            break;
                        case long number:
                            cell.Value = number;
                            break;
                        case double number:
                            cell.Value = number;
                            break;
                        case decimal number:
                            cell.Value = number;
                            break;
                        default:
                            cell.Value = table.Rows[r][c]!.ToString();
                            break;
                    }
                }
            }

            worksheet.ColumnsUsed().AdjustToContents();
            foreach (var column in worksheet.ColumnsUsed().Where(col => col.Width > 40))
                column.Width = 40;

            worksheet.SheetView.FreezeRows(1);
        }

        if (!workbook.Worksheets.Any())
            workbook.AddWorksheet("Bos");

        workbook.SaveAs(outputPath);
    }

    /// <summary>Excel sekme adları 31 karakterle sınırlıdır ve bazı karakterleri kabul etmez.</summary>
    private static string UniqueSheetName(string name, List<string> used)
    {
        var invalid = new[] { '[', ']', ':', '*', '?', '/', '\\' };
        var cleaned = new string((string.IsNullOrWhiteSpace(name) ? "Varlik" : name)
            .Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());

        if (cleaned.Length > 31) cleaned = cleaned[..31];

        var candidate = cleaned;
        var index = 2;
        while (used.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            var suffix = index.ToString();
            candidate = cleaned.Length + suffix.Length > 31
                ? cleaned[..(31 - suffix.Length)] + suffix
                : cleaned + suffix;
            index++;
        }

        used.Add(candidate);
        return candidate;
    }
}
