using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ZeFaxina;

// â”€â”€â”€ Programas de InicializaÃ§Ã£o â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed record StartupEntry(
    string Name,
    string Command,
    string Location,   // "HKCU Run" | "HKLM Run" | "Startup Folder"
    bool   Enabled);

// â”€â”€â”€ Programas Instalados â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed record InstalledProgram(
    string Name,
    string Version,
    string Publisher,
    string InstallDate,
    string UninstallCommand,
    long   SizeBytes);

// â”€â”€â”€ Problemas de Registro â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public enum RegistryIssueType { MissingFileRef, InvalidStartup, OrphanedUninstall, InvalidFont }

public sealed record RegistryIssue(
    string Key,
    string ValueName,
    string Description,
    RegistryIssueType Type);

// â”€â”€â”€ Engine de Ferramentas â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public static class ToolsEngine
{
    // â”€â”€ InicializaÃ§Ã£o â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public static List<StartupEntry> GetStartupEntries()
    {
        var result = new List<StartupEntry>();
        result.AddRange(ReadRunKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",     "HKCU Run", true,  RegistryHive.CurrentUser));
        result.AddRange(ReadRunKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU RunOnce", true,  RegistryHive.CurrentUser));
        result.AddRange(ReadRunKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",     "HKLM Run", true,  RegistryHive.LocalMachine));
        result.AddRange(ReadDisabledKey(RegistryHive.CurrentUser));
        result.AddRange(ReadStartupFolders());
        return result.DistinctBy(e => e.Name + e.Command).OrderBy(e => e.Name).ToList();
    }

    private static IEnumerable<StartupEntry> ReadRunKey(string path, string location, bool enabled, RegistryHive hive)
    {
        var result = new List<StartupEntry>();
        try
        {
            using var root = hive == RegistryHive.CurrentUser
                ? Registry.CurrentUser.OpenSubKey(path)
                : Registry.LocalMachine.OpenSubKey(path);
            if (root is null) return result;
            foreach (var name in root.GetValueNames())
            {
                var cmd = root.GetValue(name)?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(cmd))
                    result.Add(new StartupEntry(name, cmd, location, enabled));
            }
        }
        catch { }
        return result;
    }

    private static IEnumerable<StartupEntry> ReadDisabledKey(RegistryHive _)
        => [];  // Binary approval flags â€” not parsed in this version

    private static IEnumerable<StartupEntry> ReadStartupFolders()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        };
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var f in Directory.GetFiles(folder, "*.lnk"))
                yield return new StartupEntry(Path.GetFileNameWithoutExtension(f), f, "Pasta de InicializaÃ§Ã£o", true);
        }
    }

    public static void DisableStartupEntry(StartupEntry entry)
    {
        try
        {
            using var key = entry.Location == "HKCU Run"
                ? Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)
                : Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            key?.DeleteValue(entry.Name, false);
        }
        catch { }
    }

    public static void EnableStartupEntry(StartupEntry entry)
    {
        try
        {
            using var key = entry.Location == "HKCU Run"
                ? Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)
                : Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            key?.SetValue(entry.Name, entry.Command);
        }
        catch { }
    }

    // â”€â”€ Programas Instalados â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public static List<InstalledProgram> GetInstalledPrograms()
    {
        var result = new List<InstalledProgram>();
        ReadUninstallKeys(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",     result, false);
        ReadUninstallKeys(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", result, false);
        ReadUninstallKeys(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",     result, true);
        return result
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .DistinctBy(p => p.Name + p.Version)
            .OrderBy(p => p.Name)
            .ToList();
    }

    private static void ReadUninstallKeys(string path, List<InstalledProgram> result, bool userHive)
    {
        try
        {
            using var root = userHive
                ? Registry.CurrentUser.OpenSubKey(path)
                : Registry.LocalMachine.OpenSubKey(path);
            if (root is null) return;
            foreach (var sub in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(sub);
                    if (key is null) continue;
                    var name      = key.GetValue("DisplayName")?.ToString() ?? string.Empty;
                    var version   = key.GetValue("DisplayVersion")?.ToString() ?? string.Empty;
                    var publisher = key.GetValue("Publisher")?.ToString() ?? string.Empty;
                    var date      = key.GetValue("InstallDate")?.ToString() ?? string.Empty;
                    var uninstall = key.GetValue("UninstallString")?.ToString() ?? string.Empty;
                    var sysCom    = key.GetValue("SystemComponent")?.ToString();
                    if (sysCom == "1") continue; // pula componentes internos do Windows
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (long.TryParse(key.GetValue("EstimatedSize")?.ToString(), out var kb))
                        result.Add(new InstalledProgram(name, version, publisher, FormatDate(date), uninstall, kb * 1024));
                    else
                        result.Add(new InstalledProgram(name, version, publisher, FormatDate(date), uninstall, 0));
                }
                catch { }
            }
        }
        catch { }
    }

    private static string FormatDate(string raw)
    {
        if (raw.Length == 8 && DateTime.TryParseExact(raw, "yyyyMMdd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d))
            return d.ToString("dd/MM/yyyy");
        return raw;
    }

    public static void UninstallProgram(InstalledProgram program)
    {
        if (string.IsNullOrWhiteSpace(program.UninstallCommand)) return;
        try { Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{program.UninstallCommand}\"") { UseShellExecute = true }); }
        catch { }
    }

    // â”€â”€ Scanner de Registro â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public static async Task<List<RegistryIssue>> ScanRegistryAsync(
        IProgress<string> progress,
        CancellationToken ct)
    {
        var issues = new List<RegistryIssue>();
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            progress.Report("Verificando entradas de inicializaÃ§Ã£oâ€¦");
            issues.AddRange(ScanStartupRefs());

            ct.ThrowIfCancellationRequested();
            progress.Report("Verificando desinstaladoresâ€¦");
            issues.AddRange(ScanOrphanedUninstall());

            ct.ThrowIfCancellationRequested();
            progress.Report("Verificando fontesâ€¦");
            issues.AddRange(ScanFonts());
        }, ct);
        return issues;
    }

    private static IEnumerable<RegistryIssue> ScanStartupRefs()
    {
        var keys = new[]
        {
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",     Registry.CurrentUser),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",     Registry.LocalMachine),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", Registry.CurrentUser),
        };
        foreach (var (path, hive) in keys)
        {
            RegistryKey? root = null;
            try { root = hive.OpenSubKey(path); } catch { }
            if (root is null) continue;
            foreach (var name in root.GetValueNames())
            {
                var cmd = root.GetValue(name)?.ToString() ?? string.Empty;
                var exe = ExtractExePath(cmd);
                if (exe is not null && !File.Exists(exe))
                    yield return new RegistryIssue(path, name,
                        $"Arquivo nÃ£o encontrado: {exe}", RegistryIssueType.MissingFileRef);
            }
            root.Dispose();
        }
    }

    private static IEnumerable<RegistryIssue> ScanOrphanedUninstall()
    {
        var paths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };
        foreach (var path in paths)
        {
            RegistryKey? root = null;
            try { root = Registry.LocalMachine.OpenSubKey(path); } catch { }
            if (root is null) continue;
            foreach (var sub in root.GetSubKeyNames())
            {
                RegistryKey? key = null;
                try { key = root.OpenSubKey(sub); } catch { }
                if (key is null) continue;
                var uninstall = key.GetValue("UninstallString")?.ToString() ?? string.Empty;
                var name      = key.GetValue("DisplayName")?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(uninstall))
                {
                    var exe = ExtractExePath(uninstall);
                    if (exe is not null && !File.Exists(exe))
                        yield return new RegistryIssue(path + @"\" + sub, "UninstallString",
                            $"Desinstalador nÃ£o encontrado para: {name}", RegistryIssueType.OrphanedUninstall);
                }
                key.Dispose();
            }
            root.Dispose();
        }
    }

    private static IEnumerable<RegistryIssue> ScanFonts()
    {
        RegistryKey? key = null;
        try { key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts"); }
        catch { }
        if (key is null) yield break;
        var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (var name in key.GetValueNames())
        {
            var val = key.GetValue(name)?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(val)) continue;
            var fullPath = Path.IsPathRooted(val) ? val : Path.Combine(fontsDir, val);
            if (!File.Exists(fullPath))
                yield return new RegistryIssue(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts",
                    name, $"Arquivo de fonte ausente: {val}", RegistryIssueType.InvalidFont);
        }
        key.Dispose();
    }

    public static void FixRegistryIssue(RegistryIssue issue)
    {
        try
        {
            // Abre a chave pai e remove o valor com problema
            using var key = OpenWritableKey(issue.Key);
            key?.DeleteValue(issue.ValueName, false);
        }
        catch { }
    }

    // â”€â”€ Utilidades â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static string? ExtractExePath(string cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return null;
        cmd = cmd.Trim();
        if (cmd.StartsWith('"'))
        {
            var end = cmd.IndexOf('"', 1);
            return end > 1 ? cmd[1..end] : null;
        }
        var space = cmd.IndexOf(' ');
        return space > 0 ? cmd[..space] : cmd;
    }

    private static RegistryKey? OpenWritableKey(string fullPath)
    {
        if (fullPath.StartsWith("HKCU") || fullPath.StartsWith(@"SOFTWARE\M"))
            return Registry.CurrentUser.OpenSubKey(fullPath, true);
        return Registry.LocalMachine.OpenSubKey(fullPath, true);
    }
}

