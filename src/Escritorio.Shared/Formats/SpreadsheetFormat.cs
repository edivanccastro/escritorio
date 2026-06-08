using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace Escritorio.Shared.Formats;

/// <summary>
/// Abertura e gravacao de planilhas em multiplos formatos.
/// Planilha OOXML (.xlsx), ODF Calc (.ods) e CSV (.csv).
/// </summary>
public static class SpreadsheetFormat
{
    public const string OpenFilter =
        "Todas as planilhas|*.xlsx;*.ods;*.csv|" +
        "Planilha OOXML (*.xlsx)|*.xlsx|" +
        "Planilha ODF (*.ods)|*.ods|" +
        "CSV (*.csv)|*.csv";

    public const string SaveFilter =
        "Planilha OOXML (*.xlsx)|*.xlsx|" +
        "Planilha ODF (*.ods)|*.ods|" +
        "CSV (*.csv)|*.csv";

    public static void Save(DataTable table, string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".xlsx":
                SaveXlsx(table, path);
                break;
            case ".ods":
                SaveOds(table, path);
                break;
            default:
                SaveCsv(table, path);
                break;
        }
    }

    /// <summary>Le os dados como uma matriz de strings (linhas x colunas).</summary>
    public static List<List<string>> Load(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xlsx" => LoadXlsx(path),
            ".ods" => LoadOds(path),
            _ => LoadCsv(path),
        };
    }

    public static string ColumnLabel(int index)
    {
        var value = index + 1;
        var label = string.Empty;
        while (value > 0)
        {
            var rem = (value - 1) % 26;
            label = (char)('A' + rem) + label;
            value = (value - 1) / 26;
        }
        return label;
    }

    private static int ColumnIndex(string reference)
    {
        var letters = new string(reference.TakeWhile(char.IsLetter).ToArray());
        var index = 0;
        foreach (var c in letters)
        {
            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }
        return index - 1;
    }

    // ---------- XLSX ----------

    private static void SaveXlsx(DataTable table, string path)
    {
        using var spreadsheet = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = spreadsheet.AddWorkbookPart();
        workbookPart.Workbook = new S.Workbook();

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new S.SheetData();
        worksheetPart.Worksheet = new S.Worksheet(sheetData);

        var sheets = workbookPart.Workbook.AppendChild(new S.Sheets());
        sheets.Append(new S.Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "Planilha",
        });

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = new S.Row { RowIndex = (uint)(r + 1) };
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var raw = table.Rows[r][c]?.ToString() ?? string.Empty;
                if (raw.Length == 0)
                {
                    continue;
                }

                var cell = new S.Cell { CellReference = $"{ColumnLabel(c)}{r + 1}" };
                if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                {
                    cell.DataType = S.CellValues.Number;
                    cell.CellValue = new S.CellValue(number.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    cell.DataType = S.CellValues.InlineString;
                    cell.InlineString = new S.InlineString(new S.Text(raw));
                }

                row.Append(cell);
            }

            sheetData.Append(row);
        }

        workbookPart.Workbook.Save();
    }

    private static List<List<string>> LoadXlsx(string path)
    {
        var result = new List<List<string>>();
        using var spreadsheet = SpreadsheetDocument.Open(path, false);
        var workbookPart = spreadsheet.WorkbookPart;
        if (workbookPart is null)
        {
            return result;
        }

        var sheet = workbookPart.Workbook?.Descendants<S.Sheet>().FirstOrDefault();
        if (sheet?.Id?.Value is null)
        {
            return result;
        }

        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value);
        var worksheet = worksheetPart.Worksheet;
        if (worksheet is null)
        {
            return result;
        }
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;

        foreach (var row in worksheet.Descendants<S.Row>())
        {
            var cells = row.Elements<S.Cell>().ToList();
            var values = new List<string>();
            foreach (var cell in cells)
            {
                var index = cell.CellReference?.Value is { } reference ? ColumnIndex(reference) : values.Count;
                while (values.Count < index)
                {
                    values.Add(string.Empty);
                }
                values.Add(ReadCell(cell, sharedStrings));
            }
            result.Add(values);
        }

        return result;
    }

    private static string ReadCell(S.Cell cell, S.SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.InnerText ?? cell.InlineString?.Text?.Text ?? string.Empty;

        if (cell.DataType?.Value == S.CellValues.SharedString
            && sharedStrings is not null
            && int.TryParse(value, out var ssIndex)
            && ssIndex >= 0 && ssIndex < sharedStrings.ChildElements.Count)
        {
            return sharedStrings.ElementAt(ssIndex).InnerText;
        }

        return value;
    }

    // ---------- ODS ----------

    private static void SaveOds(DataTable table, string path)
    {
        var rows = new List<XElement>();
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var cells = new List<XElement>();
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var raw = table.Rows[r][c]?.ToString() ?? string.Empty;
                var cell = new XElement(OdfPackage.Table + "table-cell");
                if (raw.Length > 0)
                {
                    if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                    {
                        cell.Add(new XAttribute(OdfPackage.Office + "value-type", "float"));
                        cell.Add(new XAttribute(OdfPackage.Office + "value",
                            number.ToString(CultureInfo.InvariantCulture)));
                    }
                    else
                    {
                        cell.Add(new XAttribute(OdfPackage.Office + "value-type", "string"));
                    }
                    cell.Add(new XElement(OdfPackage.Text + "p", raw));
                }
                cells.Add(cell);
            }
            rows.Add(new XElement(OdfPackage.Table + "table-row", cells));
        }

        var content = OdfPackage.DocumentContent(
            new XElement(OdfPackage.Office + "spreadsheet",
                new XElement(OdfPackage.Table + "table",
                    new XAttribute(OdfPackage.Table + "name", "Planilha"),
                    rows)));

        OdfPackage.Write(path, "application/vnd.oasis.opendocument.spreadsheet",
            new XDocument(content));
    }

    private static List<List<string>> LoadOds(string path)
    {
        var result = new List<List<string>>();
        var xml = OdfPackage.ReadContent(path);
        var table = xml.Descendants(OdfPackage.Table + "table").FirstOrDefault();
        if (table is null)
        {
            return result;
        }

        foreach (var row in table.Elements(OdfPackage.Table + "table-row"))
        {
            var values = new List<string>();
            foreach (var cell in row.Elements(OdfPackage.Table + "table-cell"))
            {
                var repeat = (int?)cell.Attribute(OdfPackage.Table + "number-columns-repeated") ?? 1;
                repeat = Math.Min(repeat, 1024);
                var text = string.Concat(cell.Elements(OdfPackage.Text + "p").Select(p => p.Value));
                for (var i = 0; i < repeat; i++)
                {
                    values.Add(text);
                }
            }
            result.Add(values);
        }

        return result;
    }

    // ---------- CSV ----------

    private static void SaveCsv(DataTable table, string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        foreach (DataRow row in table.Rows)
        {
            var cells = row.ItemArray.Select(v => EscapeCsv(v?.ToString() ?? string.Empty));
            writer.WriteLine(string.Join(",", cells));
        }
    }

    private static List<List<string>> LoadCsv(string path)
    {
        return File.ReadAllLines(path).Select(ParseCsvLine).ToList();
    }

    private static string EscapeCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        result.Add(current.ToString());
        return result;
    }
}
