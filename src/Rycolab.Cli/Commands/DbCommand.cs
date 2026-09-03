using System.Globalization;
using System.Text;
using System.Text.Json;
using Rycolab.Core;
using Spectre.Console;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab db path | stats | import | sql "<select>" [--plain] | export <table> [--since 7d] [--out file.csv|file.jsonl]
/// The database itself: where it is, what it holds, the one-time import of
/// the JSONL era, a read-only query, and a dump of one table. No elevation.
/// </summary>
public static class DbCommand
{
    public static int Run(Args args)
    {
        var sub = args.Positional.FirstOrDefault() ?? "stats";
        switch (sub)
        {
            case "path":
            {
                var exists = File.Exists(AppPaths.Db);
                Console.WriteLine($"  {AppPaths.Db}{(exists ? $"  ({new FileInfo(AppPaths.Db).Length / 1024} KB)" : "  (not created yet)")}");
                return 0;
            }
            case "stats":
            {
                using var store = Store.Open();
                var (counts, bytes) = store.Stats();
                Console.WriteLine();
                Console.WriteLine($"  {AppPaths.Db}  {bytes / 1024.0 / 1024:F1} MB");
                foreach (var (table, rows) in counts) Console.WriteLine($"  {table,-14} {rows,10:N0}");
                var (_, first) = store.Query("SELECT MIN(ts), MAX(ts) FROM ticks");
                if (first.Count > 0 && first[0][0] is string a && first[0][1] is string b)
                    Console.WriteLine($"  ticks from {DateTime.Parse(a):yyyy-MM-dd HH:mm} to {DateTime.Parse(b):yyyy-MM-dd HH:mm}");
                Console.WriteLine();
                return 0;
            }
            case "import":
            {
                using var store = Store.Open();
                Console.WriteLine();
                foreach (var line in store.ImportLegacy(AppPaths.Data)) Console.WriteLine($"  {line}");
                Console.WriteLine();
                return 0;
            }
            case "sql":
            {
                if (args.Positional.Count < 2) { Console.Error.WriteLine("Usage: rycolab db sql \"select ... from ticks ...\" [--plain]"); return 2; }
                var sql = string.Join(" ", args.Positional.Skip(1));
                using var store = Store.Open();
                (string[] Columns, List<object?[]> Rows) result;
                try { result = store.Query(sql); }
                catch (Microsoft.Data.Sqlite.SqliteException ex) { Console.Error.WriteLine($"  {ex.Message}"); return 1; }
                if (args.Has("plain") || Console.IsOutputRedirected)
                {
                    Console.WriteLine(string.Join("\t", result.Columns));
                    foreach (var row in result.Rows) Console.WriteLine(string.Join("\t", row.Select(Cell)));
                }
                else
                {
                    var table = new Table().Border(TableBorder.Rounded);
                    foreach (var c in result.Columns) table.AddColumn(new TableColumn(Markup.Escape(c)).NoWrap());
                    foreach (var row in result.Rows) table.AddRow(row.Select(v => Markup.Escape(Cell(v))).ToArray());
                    AnsiConsole.Write(table);
                    Console.WriteLine($"  {result.Rows.Count} row{(result.Rows.Count == 1 ? "" : "s")}");
                }
                return 0;
            }
            case "export":
            {
                if (args.Positional.Count < 2) { Console.Error.WriteLine($"Usage: rycolab db export <table> [--since 7d] [--out file.csv|file.jsonl]   tables: {string.Join(", ", Store.Tables)}"); return 2; }
                var table = args.Positional[1];
                DateTime? since = null;
                if (args.Get("since") is { } s)
                {
                    if (PowerReport.Period(s, null, DateTime.Now) is not { } p) { Console.Error.WriteLine("  --since takes 30d, 7d, 2w or 24h."); return 2; }
                    since = p.Since;
                }
                using var store = Store.Open();
                (string[] Columns, List<object?[]> Rows) data;
                try { data = store.Export(table, since); }
                catch (ArgumentException ex) { Console.Error.WriteLine($"  {ex.Message}"); return 2; }

                var outPath = args.Get("out");
                var jsonl = outPath?.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) == true;
                var w = outPath is null ? Console.Out : new StreamWriter(Path.GetFullPath(outPath), false, new UTF8Encoding(false));
                try
                {
                    if (jsonl)
                        foreach (var row in data.Rows)
                            w.WriteLine(JsonSerializer.Serialize(data.Columns.Zip(row).ToDictionary(x => x.First, x => x.Second)));
                    else
                    {
                        w.WriteLine(string.Join(",", data.Columns));
                        foreach (var row in data.Rows) w.WriteLine(string.Join(",", row.Select(Csv)));
                    }
                }
                finally { if (outPath is not null) w.Dispose(); }
                if (outPath is not null) Console.WriteLine($"  Written {Path.GetFullPath(outPath)}: {data.Rows.Count} rows");
                return 0;
            }
            default:
                Console.Error.WriteLine($"Unknown db command: {sub}. One of: path, stats, import, sql, export.");
                return 2;
        }
    }

    private static string Cell(object? v) => v switch
    {
        null => "",
        double d => d.ToString("0.###", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    private static string Csv(object? v)
    {
        var s = Cell(v);
        return s.IndexOfAny([',', '"', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
