using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Escritorio.Shared;

/// <summary>
/// Avaliador de formulas no estilo planilha. Suporta operadores aritmeticos,
/// referencias de celula (=A1+B1), intervalos (A1:B3) e funcoes de agregacao
/// (SOMA/SUM, MEDIA/AVERAGE, MAXIMO/MAX, MINIMO/MIN, CONT/COUNT).
/// </summary>
public static class FormulaEngine
{
    private static readonly Regex CellReference = new(@"([A-Z]+)(\d+)", RegexOptions.Compiled);

    private static readonly Regex FunctionCall = new(
        @"(?i)\b(SOMA|SUM|MEDIA|MÉDIA|AVERAGE|MAXIMO|MÁXIMO|MAX|MINIMO|MÍNIMO|MIN|CONT|COUNT)\s*\(([^()]*)\)",
        RegexOptions.Compiled);

    public static bool IsFormula(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.TrimStart().StartsWith("=");

    public static string Evaluate(string formula, Func<string, int, string?> cellValueResolver)
    {
        var raw = formula.TrimStart().TrimStart('=');

        try
        {
            raw = ExpandFunctions(raw, cellValueResolver);

            var normalized = CellReference.Replace(raw, match =>
            {
                var column = match.Groups[1].Value;
                var row = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                return ResolveNumber(cellValueResolver(column, row)).ToString(CultureInfo.InvariantCulture);
            });

            using var table = new DataTable();
            var result = table.Compute(normalized, string.Empty);
            return Convert.ToString(result, CultureInfo.InvariantCulture) ?? "0";
        }
        catch
        {
            return "#ERRO";
        }
    }

    private static string ExpandFunctions(string input, Func<string, int, string?> resolver)
    {
        var guard = 0;
        while (FunctionCall.IsMatch(input) && guard++ < 50)
        {
            input = FunctionCall.Replace(input, match =>
            {
                var function = match.Groups[1].Value.ToUpperInvariant();
                var values = CollectValues(match.Groups[2].Value, resolver);
                var aggregate = Aggregate(function, values);
                return aggregate.ToString(CultureInfo.InvariantCulture);
            });
        }
        return input;
    }

    private static List<double> CollectValues(string args, Func<string, int, string?> resolver)
    {
        var values = new List<double>();
        foreach (var part in args.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Contains(':'))
            {
                values.AddRange(ExpandRange(part, resolver));
            }
            else if (CellReference.IsMatch(part) && CellReference.Match(part).Value == part)
            {
                var m = CellReference.Match(part);
                values.Add(ResolveNumber(resolver(m.Groups[1].Value, int.Parse(m.Groups[2].Value))));
            }
            else if (double.TryParse(part, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            {
                values.Add(number);
            }
        }
        return values;
    }

    private static IEnumerable<double> ExpandRange(string range, Func<string, int, string?> resolver)
    {
        var ends = range.Split(':');
        if (ends.Length != 2)
        {
            yield break;
        }

        var startMatch = CellReference.Match(ends[0].Trim());
        var endMatch = CellReference.Match(ends[1].Trim());
        if (!startMatch.Success || !endMatch.Success)
        {
            yield break;
        }

        var startCol = ColumnToIndex(startMatch.Groups[1].Value);
        var endCol = ColumnToIndex(endMatch.Groups[1].Value);
        var startRow = int.Parse(startMatch.Groups[2].Value);
        var endRow = int.Parse(endMatch.Groups[2].Value);

        for (var c = Math.Min(startCol, endCol); c <= Math.Max(startCol, endCol); c++)
        {
            for (var r = Math.Min(startRow, endRow); r <= Math.Max(startRow, endRow); r++)
            {
                yield return ResolveNumber(resolver(IndexToColumn(c), r));
            }
        }
    }

    private static double Aggregate(string function, List<double> values)
    {
        return function switch
        {
            "SOMA" or "SUM" => values.Sum(),
            "MEDIA" or "MÉDIA" or "AVERAGE" => values.Count > 0 ? values.Average() : 0,
            "MAXIMO" or "MÁXIMO" or "MAX" => values.Count > 0 ? values.Max() : 0,
            "MINIMO" or "MÍNIMO" or "MIN" => values.Count > 0 ? values.Min() : 0,
            "CONT" or "COUNT" => values.Count,
            _ => 0,
        };
    }

    private static double ResolveNumber(string? value) =>
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : 0;

    private static int ColumnToIndex(string column)
    {
        var index = 0;
        foreach (var c in column.ToUpperInvariant())
        {
            index = index * 26 + (c - 'A' + 1);
        }
        return index;
    }

    private static string IndexToColumn(int index)
    {
        var label = string.Empty;
        while (index > 0)
        {
            var rem = (index - 1) % 26;
            label = (char)('A' + rem) + label;
            index = (index - 1) / 26;
        }
        return label;
    }
}
