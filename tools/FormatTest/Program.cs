using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Documents;
using Escritorio.Shared;
using Escritorio.Shared.Formats;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "escritorio-formattest");
        Directory.CreateDirectory(dir);
        var failures = 0;

        foreach (var ext in new[] { ".docx", ".odt", ".rtf", ".txt" })
        {
            failures += TestText(dir, ext);
        }

        foreach (var ext in new[] { ".xlsx", ".ods", ".csv" })
        {
            failures += TestSheet(dir, ext);
        }

        foreach (var ext in new[] { ".pptx", ".odp", ".json" })
        {
            failures += TestPresentation(dir, ext);
        }

        failures += TestFormulas();

        Console.WriteLine(failures == 0
            ? "TODOS OS FORMATOS OK"
            : $"FALHAS: {failures}");
        return failures == 0 ? 0 : 1;
    }

    private static int TestText(string dir, string ext)
    {
        var path = Path.Combine(dir, "doc" + ext);
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Run("Ola Letrucio")));
        doc.Blocks.Add(new Paragraph(new Run("Segunda linha")));

        TextDocumentFormat.Save(doc, path);
        var loaded = TextDocumentFormat.Load(path);
        var text = new TextRange(loaded.ContentStart, loaded.ContentEnd).Text;

        var ok = text.Contains("Ola Letrucio") && text.Contains("Segunda linha");
        Report(ext, ok, text);
        return ok ? 0 : 1;
    }

    private static int TestSheet(string dir, string ext)
    {
        var path = Path.Combine(dir, "sheet" + ext);
        var table = new DataTable();
        table.Columns.Add("A");
        table.Columns.Add("B");
        table.Rows.Add("Nome", "Valor");
        table.Rows.Add("Item", "42.5");

        SpreadsheetFormat.Save(table, path);
        var loaded = SpreadsheetFormat.Load(path);
        var flat = string.Join(" | ", loaded.Select(r => string.Join(",", r)));

        var ok = flat.Contains("Nome") && flat.Contains("Valor")
            && flat.Contains("Item") && flat.Contains("42.5");
        Report(ext, ok, flat);
        return ok ? 0 : 1;
    }

    private static int TestPresentation(string dir, string ext)
    {
        var path = Path.Combine(dir, "pres" + ext);
        var slides = new List<SlideData>
        {
            new() { Title = "Slide Um", Content = "Conteudo A\nLinha 2" },
            new() { Title = "Slide Dois", Content = "Conteudo B" },
        };

        PresentationFormat.Save(slides, path);
        var loaded = PresentationFormat.Load(path);
        var flat = string.Join(" || ", loaded.Select(s => $"{s.Title}: {s.Content}"));

        var ok = loaded.Count == 2 && flat.Contains("Slide Um")
            && flat.Contains("Conteudo A") && flat.Contains("Slide Dois");
        Report(ext, ok, flat);
        return ok ? 0 : 1;
    }

    private static int TestFormulas()
    {
        var cells = new Dictionary<string, string>
        {
            ["A1"] = "10", ["A2"] = "20", ["A3"] = "30", ["B1"] = "5",
        };
        string? Resolve(string col, int row) => cells.TryGetValue($"{col}{row}", out var v) ? v : "0";

        var cases = new (string Formula, string Expected)[]
        {
            ("=SOMA(A1:A3)", "60"),
            ("=MÉDIA(A1:A3)", "20"),
            ("=MÁXIMO(A1:A3)", "30"),
            ("=MÍNIMO(A1:A3)", "10"),
            ("=CONT(A1:A3)", "3"),
            ("=A1+B1*2", "20"),
        };

        var fails = 0;
        foreach (var (formula, expected) in cases)
        {
            var result = FormulaEngine.Evaluate(formula, Resolve);
            var ok = result == expected;
            if (!ok) fails++;
            Console.WriteLine($"[{(ok ? "OK " : "ERRO")}] {formula,-16} = {result} (esperado {expected})");
        }
        return fails;
    }

    private static void Report(string ext, bool ok, string detail)
    {
        Console.WriteLine($"[{(ok ? "OK " : "ERRO")}] {ext,-6} -> {detail}");
    }
}
