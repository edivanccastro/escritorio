using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace ZeFaxina;

// â”€â”€â”€ Modelos â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public enum CleanCategory
{
    WindowsTemp, UserTemp, RecycleBin, ThumbnailCache, Prefetch,
    EventLogs, Clipboard, RecentFiles, DeliveryOptimization,
    ChromeCache, ChromeHistory, ChromeCookies,
    FirefoxCache, FirefoxHistory, FirefoxCookies,
    EdgeCache, EdgeHistory, EdgeCookies,
}

public sealed record CleanTarget(CleanCategory Category, string Label, string Group, bool DefaultOn = true);

public sealed record CleanResult(
    CleanCategory Category,
    string Label,
    long   BytesFound,
    int    FilesFound,
    string? Error = null);

public sealed record CleanProgress(string CurrentItem, int Done, int Total);

public sealed record CleanHistoryEntry(
    string Id,
    DateTime Date,
    long BytesFreed,
    int FilesDeleted,
    string Categories);

// â”€â”€â”€ Engine principal â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed class CleanEngine
{
    // DefiniÃ§Ã£o de todos os alvos disponÃ­veis
    public static readonly IReadOnlyList<CleanTarget> AllTargets =
    [
        new(CleanCategory.WindowsTemp,           "Arquivos TemporÃ¡rios do Sistema",      "Windows"),
        new(CleanCategory.UserTemp,              "Arquivos TemporÃ¡rios do UsuÃ¡rio",      "Windows"),
        new(CleanCategory.RecycleBin,            "Lixeira",                              "Windows"),
        new(CleanCategory.ThumbnailCache,        "Cache de Miniaturas",                  "Windows"),
        new(CleanCategory.Prefetch,              "Cache Prefetch",                       "Windows"),
        new(CleanCategory.RecentFiles,           "Arquivos Recentes",                    "Windows"),
        new(CleanCategory.DeliveryOptimization,  "Cache Windows Update (Delivery)",      "Windows",  false),
        new(CleanCategory.EventLogs,             "Logs de Eventos do Windows",           "Windows",  false),
        new(CleanCategory.Clipboard,             "Ãrea de TransferÃªncia",                "Windows"),
        new(CleanCategory.ChromeCache,           "Cache",                                "Google Chrome"),
        new(CleanCategory.ChromeHistory,         "HistÃ³rico de NavegaÃ§Ã£o",               "Google Chrome",  false),
        new(CleanCategory.ChromeCookies,         "Cookies",                              "Google Chrome",  false),
        new(CleanCategory.FirefoxCache,          "Cache",                                "Mozilla Firefox"),
        new(CleanCategory.FirefoxHistory,        "HistÃ³rico de NavegaÃ§Ã£o",               "Mozilla Firefox", false),
        new(CleanCategory.FirefoxCookies,        "Cookies",                              "Mozilla Firefox", false),
        new(CleanCategory.EdgeCache,             "Cache",                                "Microsoft Edge"),
        new(CleanCategory.EdgeHistory,           "HistÃ³rico de NavegaÃ§Ã£o",               "Microsoft Edge",  false),
        new(CleanCategory.EdgeCookies,           "Cookies",                              "Microsoft Edge",  false),
    ];

    // â”€â”€ AnÃ¡lise (apenas mede, nÃ£o deleta) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public async Task<List<CleanResult>> AnalyzeAsync(
        IEnumerable<CleanCategory> categories,
        IProgress<CleanProgress> progress,
        CancellationToken ct)
    {
        var list = categories.ToList();
        var results = new List<CleanResult>();
        int done = 0;

        await Task.Run(() =>
        {
            foreach (var cat in list)
            {
                ct.ThrowIfCancellationRequested();
                var label = AllTargets.First(t => t.Category == cat).Label;
                progress.Report(new CleanProgress(label, done, list.Count));
                results.Add(Measure(cat));
                done++;
            }
        }, ct);

        return results;
    }

    // â”€â”€ Limpeza real â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public async Task<List<CleanResult>> CleanAsync(
        IEnumerable<CleanCategory> categories,
        IProgress<CleanProgress> progress,
        CancellationToken ct)
    {
        var list = categories.ToList();
        var results = new List<CleanResult>();
        int done = 0;

        await Task.Run(() =>
        {
            foreach (var cat in list)
            {
                ct.ThrowIfCancellationRequested();
                var label = AllTargets.First(t => t.Category == cat).Label;
                progress.Report(new CleanProgress(label, done, list.Count));
                results.Add(DoClean(cat));
                done++;
            }
        }, ct);

        return results;
    }

    // â”€â”€ Medir tamanho por categoria â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static CleanResult Measure(CleanCategory cat)
    {
        try
        {
            return cat switch
            {
                CleanCategory.WindowsTemp          => MeasurePaths([Path.GetTempPath(), @"C:\Windows\Temp"], cat),
                CleanCategory.UserTemp             => MeasurePaths([Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Temp"], cat),
                CleanCategory.RecycleBin           => MeasureRecycleBin(cat),
                CleanCategory.ThumbnailCache       => MeasurePaths([Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\Windows\Explorer"], cat, "thumbcache_*.db"),
                CleanCategory.Prefetch             => MeasurePaths([@"C:\Windows\Prefetch"], cat, "*.pf"),
                CleanCategory.RecentFiles          => MeasurePaths([Environment.GetFolderPath(Environment.SpecialFolder.Recent)], cat),
                CleanCategory.DeliveryOptimization => MeasurePaths([@"C:\Windows\SoftwareDistribution\DeliveryOptimization"], cat),
                CleanCategory.EventLogs            => MeasurePaths([@"C:\Windows\System32\winevt\Logs"], cat, "*.evtx"),
                CleanCategory.Clipboard            => new CleanResult(cat, "Ãrea de TransferÃªncia", 0, 1),
                CleanCategory.ChromeCache          => MeasurePaths(ChromeCachePaths(), cat),
                CleanCategory.ChromeHistory        => MeasureFiles(ChromeProfilePaths("History"), cat),
                CleanCategory.ChromeCookies        => MeasureFiles(ChromeProfilePaths("Cookies"), cat),
                CleanCategory.FirefoxCache         => MeasurePaths(FirefoxCachePaths(), cat),
                CleanCategory.FirefoxHistory       => MeasureFiles(FirefoxProfileFiles("places.sqlite"), cat),
                CleanCategory.FirefoxCookies       => MeasureFiles(FirefoxProfileFiles("cookies.sqlite"), cat),
                CleanCategory.EdgeCache            => MeasurePaths(EdgeCachePaths(), cat),
                CleanCategory.EdgeHistory          => MeasureFiles(EdgeProfilePaths("History"), cat),
                CleanCategory.EdgeCookies          => MeasureFiles(EdgeProfilePaths("Cookies"), cat),
                _                                  => new CleanResult(cat, cat.ToString(), 0, 0),
            };
        }
        catch (Exception ex)
        {
            return new CleanResult(cat, cat.ToString(), 0, 0, ex.Message);
        }
    }

    // â”€â”€ Executar limpeza por categoria â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static CleanResult DoClean(CleanCategory cat)
    {
        var measured = Measure(cat);
        if (measured.BytesFound == 0 && measured.FilesFound == 0)
            return measured;

        try
        {
            long freed = 0;
            int  deleted = 0;

            switch (cat)
            {
                case CleanCategory.WindowsTemp:
                    (freed, deleted) = DeletePaths([Path.GetTempPath(), @"C:\Windows\Temp"]);
                    break;
                case CleanCategory.UserTemp:
                    (freed, deleted) = DeletePaths([Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Temp"]);
                    break;
                case CleanCategory.RecycleBin:
                    (freed, deleted) = EmptyRecycleBin();
                    break;
                case CleanCategory.ThumbnailCache:
                    (freed, deleted) = DeleteFiles(
                        Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\Windows\Explorer", "thumbcache_*.db"));
                    break;
                case CleanCategory.Prefetch:
                    (freed, deleted) = DeleteFiles(SafeGetFiles(@"C:\Windows\Prefetch", "*.pf"));
                    break;
                case CleanCategory.RecentFiles:
                    (freed, deleted) = DeletePaths([Environment.GetFolderPath(Environment.SpecialFolder.Recent)]);
                    break;
                case CleanCategory.DeliveryOptimization:
                    (freed, deleted) = DeletePaths([@"C:\Windows\SoftwareDistribution\DeliveryOptimization"]);
                    break;
                case CleanCategory.Clipboard:
                    try { System.Windows.Clipboard.Clear(); deleted = 1; } catch { }
                    break;
                case CleanCategory.ChromeCache:
                    (freed, deleted) = DeletePaths(ChromeCachePaths());
                    break;
                case CleanCategory.ChromeHistory:
                    (freed, deleted) = DeleteFiles(ChromeProfilePaths("History"));
                    break;
                case CleanCategory.ChromeCookies:
                    (freed, deleted) = DeleteFiles(ChromeProfilePaths("Cookies"));
                    break;
                case CleanCategory.FirefoxCache:
                    (freed, deleted) = DeletePaths(FirefoxCachePaths());
                    break;
                case CleanCategory.FirefoxHistory:
                    (freed, deleted) = DeleteFiles(FirefoxProfileFiles("places.sqlite"));
                    break;
                case CleanCategory.FirefoxCookies:
                    (freed, deleted) = DeleteFiles(FirefoxProfileFiles("cookies.sqlite"));
                    break;
                case CleanCategory.EdgeCache:
                    (freed, deleted) = DeletePaths(EdgeCachePaths());
                    break;
                case CleanCategory.EdgeHistory:
                    (freed, deleted) = DeleteFiles(EdgeProfilePaths("History"));
                    break;
                case CleanCategory.EdgeCookies:
                    (freed, deleted) = DeleteFiles(EdgeProfilePaths("Cookies"));
                    break;
            }

            return new CleanResult(cat, measured.Label, freed, deleted);
        }
        catch (Exception ex)
        {
            return new CleanResult(cat, measured.Label, 0, 0, ex.Message);
        }
    }

    // â”€â”€ Helpers de mediÃ§Ã£o â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static CleanResult MeasurePaths(IEnumerable<string> paths, CleanCategory cat, string pattern = "*")
    {
        long bytes = 0; int files = 0;
        var label = AllTargets.FirstOrDefault(t => t.Category == cat)?.Label ?? cat.ToString();
        foreach (var p in paths.Where(Directory.Exists))
        {
            foreach (var f in EnumerateFilesSafe(p, pattern))
            {
                try { bytes += new FileInfo(f).Length; files++; } catch { }
            }
        }
        return new CleanResult(cat, label, bytes, files);
    }

    private static CleanResult MeasureFiles(IEnumerable<string> filePaths, CleanCategory cat)
    {
        long bytes = 0; int files = 0;
        var label = AllTargets.FirstOrDefault(t => t.Category == cat)?.Label ?? cat.ToString();
        foreach (var f in filePaths.Where(File.Exists))
        {
            try { bytes += new FileInfo(f).Length; files++; } catch { }
        }
        return new CleanResult(cat, label, bytes, files);
    }

    private static CleanResult MeasureRecycleBin(CleanCategory cat)
    {
        long bytes = 0; int files = 0;
        var label = AllTargets.FirstOrDefault(t => t.Category == cat)?.Label ?? "Lixeira";
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var rb = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
            if (!Directory.Exists(rb)) continue;
            foreach (var f in EnumerateFilesSafe(rb))
            {
                try { bytes += new FileInfo(f).Length; files++; } catch { }
            }
        }
        return new CleanResult(cat, label, bytes, files);
    }

    // â”€â”€ Helpers de deleÃ§Ã£o â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static (long freed, int deleted) DeletePaths(IEnumerable<string> paths, string pattern = "*")
    {
        long freed = 0; int deleted = 0;
        foreach (var p in paths.Where(Directory.Exists))
        {
            foreach (var f in EnumerateFilesSafe(p, pattern))
            {
                try
                {
                    var fi = new FileInfo(f);
                    freed += fi.Length;
                    fi.Delete();
                    deleted++;
                }
                catch { }
            }
            // Apaga subpastas vazias
            foreach (var d in SafeGetDirs(p))
                try { Directory.Delete(d, true); } catch { }
        }
        return (freed, deleted);
    }

    private static (long freed, int deleted) DeleteFiles(IEnumerable<string> files)
    {
        long freed = 0; int deleted = 0;
        foreach (var f in files.Where(File.Exists))
        {
            try
            {
                freed += new FileInfo(f).Length;
                File.Delete(f);
                deleted++;
            }
            catch { }
        }
        return (freed, deleted);
    }

    private static (long freed, int deleted) EmptyRecycleBin()
    {
        var measured = MeasureRecycleBin(CleanCategory.RecycleBin);
        try
        {
            // SHEmptyRecycleBin via Shell API
            NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, 0x00000007);
            return (measured.BytesFound, measured.FilesFound);
        }
        catch
        {
            // Fallback manual
            return DeletePaths(DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => Path.Combine(d.RootDirectory.FullName, "$Recycle.Bin")));
        }
    }

    // â”€â”€ Caminhos de navegadores â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static IEnumerable<string> ChromeCachePaths() =>
    [
        Path.Combine(Local, @"Google\Chrome\User Data\Default\Cache"),
        Path.Combine(Local, @"Google\Chrome\User Data\Default\Code Cache"),
        Path.Combine(Local, @"Google\Chrome\User Data\Default\GPUCache"),
    ];

    private static IEnumerable<string> ChromeProfilePaths(string fileName)
    {
        var profiles = new[] { "Default", "Profile 1", "Profile 2", "Profile 3" };
        return profiles.Select(p => Path.Combine(Local, "Google", "Chrome", "User Data", p, fileName));
    }

    private static IEnumerable<string> FirefoxCachePaths()
    {
        var base1 = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
        if (!Directory.Exists(base1)) return [];
        return Directory.GetDirectories(base1).Select(p => Path.Combine(p, "cache2"));
    }

    private static IEnumerable<string> FirefoxProfileFiles(string fileName)
    {
        var base1 = Path.Combine(AppData, @"Mozilla\Firefox\Profiles");
        if (!Directory.Exists(base1)) return [];
        return Directory.GetDirectories(base1).Select(p => Path.Combine(p, fileName));
    }

    private static IEnumerable<string> EdgeCachePaths() =>
    [
        Path.Combine(Local, @"Microsoft\Edge\User Data\Default\Cache"),
        Path.Combine(Local, @"Microsoft\Edge\User Data\Default\Code Cache"),
        Path.Combine(Local, @"Microsoft\Edge\User Data\Default\GPUCache"),
    ];

    private static IEnumerable<string> EdgeProfilePaths(string fileName)
    {
        var profiles = new[] { "Default", "Profile 1", "Profile 2" };
        return profiles.Select(p => Path.Combine(Local, "Microsoft", "Edge", "User Data", p, fileName));
    }

    // â”€â”€ UtilitÃ¡rios de I/O â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern = "*")
    {
        var q = new Queue<string>(); q.Enqueue(root);
        while (q.Count > 0)
        {
            var d = q.Dequeue();
            IEnumerable<string> files = [];
            try { files = Directory.EnumerateFiles(d, pattern); } catch { }
            foreach (var f in files) yield return f;
            try { foreach (var s in Directory.EnumerateDirectories(d)) q.Enqueue(s); } catch { }
        }
    }

    private static IEnumerable<string> SafeGetFiles(string dir, string pattern)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : []; }
        catch { return []; }
    }

    private static IEnumerable<string> SafeGetDirs(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : []; }
        catch { return []; }
    }

    // â”€â”€ HistÃ³rico â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static string HistoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Escritorio", "ZeFaxina", "history.json");

    public static List<CleanHistoryEntry> LoadHistory()
    {
        if (!File.Exists(HistoryPath)) return [];
        try { return JsonSerializer.Deserialize<List<CleanHistoryEntry>>(File.ReadAllText(HistoryPath)) ?? []; }
        catch { return []; }
    }

    public static void AppendHistory(CleanHistoryEntry entry)
    {
        var dir = Path.GetDirectoryName(HistoryPath)!;
        Directory.CreateDirectory(dir);
        var list = LoadHistory();
        list.Insert(0, entry);
        if (list.Count > 50) list = list.Take(50).ToList();
        File.WriteAllText(HistoryPath,
            JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void ClearHistory()
    {
        if (File.Exists(HistoryPath)) File.Delete(HistoryPath);
    }

    // â”€â”€ FormataÃ§Ã£o â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024             => $"{bytes} B",
        < 1024 * 1024      => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _                  => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB",
    };
}

// â”€â”€ P/Invoke para esvaziar a Lixeira â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
}

