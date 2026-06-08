using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vacinaldo;

//  -- ? -- ? -- ? Modelos  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

public enum ThreatLevel { Low, Medium, High, Critical }

public sealed record ThreatInfo(
    string FilePath,
    string ThreatName,
    ThreatLevel Level,
    string Description,
    DateTime DetectedAt,
    DetectionExplanation? Explanation = null);

public sealed record ScanProgress(
    string CurrentFile,
    int FilesScanned,
    int ThreatsFound,
    bool IsFinished);

public sealed record QuarantineEntry(
    string Id,
    string OriginalPath,
    string ThreatName,
    ThreatLevel Level,
    DateTime QuarantinedAt,
    string QuarantinePath);

public sealed record ScanHistoryEntry(
    string Id,
    DateTime StartedAt,
    DateTime FinishedAt,
    string ScanType,
    int FilesScanned,
    int ThreatsFound);

//  -- ? -- ? -- ? Motor de varredura  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

public sealed class ScanEngine
{
    // EICAR test string (detectável em arquivos de teste)
    private const string EicarSignature = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    // Extensões duplamente suspeitas (ex: fatura.pdf.exe)
    private static readonly string[] DangerousDoubleExts =
        [".exe", ".scr", ".com", ".bat", ".cmd", ".vbs", ".js", ".ps1", ".msi"];

    // Nomes de arquivo tipicamente usados por malware
    private static readonly Regex MalwareNamePattern = new(
        @"(?i)(winupdate|svchost32|explorer32|csrss32|lsass32|" +
        @"setup_crack|keygen|activator|patch_v|free_crack|" +
        @"cryptolocker|ransomware|trojan|worm_|spyware|adware_)",
        RegexOptions.Compiled);

    // Extensões de executáveis a verificar por heurística
    private static readonly HashSet<string> ExecExtensions =
        [".exe", ".dll", ".scr", ".com", ".bat", ".cmd", ".vbs", ".js", ".ps1"];

    // Pastas temporárias comuns
    private static readonly string[] TempPaths =
    [
        Path.GetTempPath(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
    ];

    // Pastas de varredura rápida
    public static IEnumerable<string> QuickScanPaths =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads",
        Path.GetTempPath(),
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
    ];

    // Pastas de varredura completa
    public static IEnumerable<string> FullScanPaths =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    ];

    //  -- ? -- ? Varredura assíncrona  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    public async Task<List<ThreatInfo>> ScanAsync(
        IEnumerable<string> roots,
        IProgress<ScanProgress> progress,
        CancellationToken ct)
    {
        var threats = new List<ThreatInfo>();
        int filesScanned = 0;

        await Task.Run(() =>
        {
            var files = roots
                .Where(Directory.Exists)
                .SelectMany(r => EnumerateFilesSafe(r));

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                filesScanned++;

                var threat = InspectFile(file);
                if (threat is not null)
                    threats.Add(threat);

                if (filesScanned % 20 == 0 || threat is not null)
                    progress.Report(new ScanProgress(file, filesScanned, threats.Count, false));
            }
        }, ct);

        progress.Report(new ScanProgress(string.Empty, filesScanned, threats.Count, true));
        return threats;
    }

    //  -- ? -- ? Inspeção de arquivo individual (SAIF-compliant)  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    public ThreatInfo? InspectFile(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;

            var name = fi.Name;
            var ext  = fi.Extension.ToLowerInvariant();

            // 1. EICAR  --  assinatura de teste anti-malware padrão
            if (fi.Length is > 0 and < 512)
            {
                try
                {
                    var content = File.ReadAllText(path, Encoding.ASCII);
                    if (content.Contains(EicarSignature, StringComparison.OrdinalIgnoreCase))
                    {
                        var signals = new List<DetectionSignal>
                        {
                            new("EICAR.Signature",
                                "Assinatura EICAR encontrada no conteúdo do arquivo", 95, "T1204")
                        };
                        return MakeThreat(path, "EICAR.TestFile", ThreatLevel.Critical,
                            "Arquivo de teste padrão EICAR detectado.", signals);
                    }
                }
                catch { /* arquivo bloqueado ou binário */ }
            }

            // 2. Dupla extensão  --  técnica clássica de ocultamento de tipo real
            var nameWithoutFirst = Path.GetFileNameWithoutExtension(name);
            var innerExt = Path.GetExtension(nameWithoutFirst).ToLowerInvariant();
            if (DangerousDoubleExts.Contains(ext) && !string.IsNullOrEmpty(innerExt) && innerExt != ext)
            {
                var signals = new List<DetectionSignal>
                {
                    new("DoubleExt.Pattern",
                        $"Dupla extensão detectada: '{innerExt}{ext}'", 70, "T1036"),
                    new("DoubleExt.DangerousExt",
                        $"Extensão executável perigosa como extensão final: '{ext}'", 20, "T1036")
                };
                return MakeThreat(path, "Trojan.DoubleExtension", ThreatLevel.High,
                    $"Arquivo com dupla extensão suspeita: {innerExt}{ext}", signals);
            }

            // 3. Nome corresponde a padrão de malware
            if (MalwareNamePattern.IsMatch(name))
            {
                var signals = new List<DetectionSignal>
                {
                    new("MalwareName.Pattern",
                        $"Nome '{name}' corresponde a assinatura de malware conhecido", 65, "T1204")
                };
                return MakeThreat(path, "Suspicious.MalwareName", ThreatLevel.Medium,
                    "Nome do arquivo corresponde a padrão de malware conhecido.", signals);
            }

            // 4. Executável em pasta temporária  --  dropper / stager
            if (ExecExtensions.Contains(ext))
            {
                var fullLower = path.ToLowerInvariant();
                if (TempPaths.Any(t => fullLower.StartsWith(t.ToLowerInvariant())))
                {
                    var signals = new List<DetectionSignal>
                    {
                        new("TempExe.Location",
                            $"Executável em pasta temporária: {Path.GetDirectoryName(path)}", 60, "T1059"),
                        new("TempExe.Extension",
                            $"Extensão executável: '{ext}'", 15, "T1059")
                    };
                    return MakeThreat(path, "PUA.TempExecutable", ThreatLevel.Medium,
                        "Executável encontrado em pasta temporária.", signals);
                }
            }

            // 5. Executável muito pequeno  --  possível stub dropper (< 2 KB)
            if (ext == ".exe" && fi.Length is > 0 and < 2048)
            {
                var signals = new List<DetectionSignal>
                {
                    new("TinyExe.Size",
                        $"Tamanho suspeito: {fi.Length} bytes (executável legítimo tipicamente > 2 KB)", 45, "T1027")
                };
                return MakeThreat(path, "Suspicious.TinyExecutable", ThreatLevel.Low,
                    "Executável suspeito: tamanho anormalmente pequeno.", signals);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    //  -- ? -- ? Construção SAIF-compliant de ThreatInfo  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private static ThreatInfo MakeThreat(
        string path, string name, ThreatLevel level,
        string desc, List<DetectionSignal> signals)
    {
        var confidence = RiskScorer.ComputeConfidence(signals);
        var fpRisk     = RiskScorer.ComputeFpRisk(signals, path);
        var technique  = signals.MaxBy(s => s.Weight)?.MitreTechnique ?? "T1204";
        var action     = RiskScorer.RecommendAction(confidence, level, fpRisk);

        var expl = new DetectionExplanation(
            DetectionId:      Guid.NewGuid().ToString("N")[..8],
            FilePath:         path,
            Signals:          signals,
            Confidence:       confidence,
            FpRisk:           fpRisk,
            PrimaryTechnique: technique,
            RecommendedAction: action,
            AnalyzedAt:       DateTime.Now);

        // Log de auditoria imutável (SAIF: trilha de eventos)
        AuditLogger.Log(new AuditEvent(
            expl.DetectionId, DateTime.Now, "ThreatDetected", "FileScan",
            $"{name}: {desc}", path, technique, confidence, "Detected"));

        return new ThreatInfo(path, name, level, desc, DateTime.Now, expl);
    }

    //  -- ? -- ? Quarentena  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    public static string QuarantineFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Escritorio", "Vacinaldo", "Quarantine");

    private static string ManifestPath =>
        Path.Combine(QuarantineFolder, "_manifest.json");

    public static List<QuarantineEntry> LoadQuarantine()
    {
        if (!File.Exists(ManifestPath)) return [];
        try
        {
            var json = File.ReadAllText(ManifestPath);
            return JsonSerializer.Deserialize<List<QuarantineEntry>>(json) ?? [];
        }
        catch { return []; }
    }

    public static void SaveQuarantine(List<QuarantineEntry> entries)
    {
        Directory.CreateDirectory(QuarantineFolder);
        File.WriteAllText(ManifestPath,
            JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static QuarantineEntry? QuarantineFile(ThreatInfo threat)
    {
        try
        {
            Directory.CreateDirectory(QuarantineFolder);
            var id = Guid.NewGuid().ToString("N")[..8];
            var qPath = Path.Combine(QuarantineFolder, id + ".quar");
            // XOR simples para "ocultar" o conteúdo (não é criptografia real, apenas ofuscação)
            var data = File.ReadAllBytes(threat.FilePath);
            for (int i = 0; i < data.Length; i++) data[i] ^= 0xAB;
            File.WriteAllBytes(qPath, data);
            File.Delete(threat.FilePath);

            var entry = new QuarantineEntry(id, threat.FilePath, threat.ThreatName,
                threat.Level, DateTime.Now, qPath);

            var list = LoadQuarantine();
            list.Add(entry);
            SaveQuarantine(list);
            return entry;
        }
        catch { return null; }
    }

    public static bool RestoreFile(QuarantineEntry entry)
    {
        try
        {
            var data = File.ReadAllBytes(entry.QuarantinePath);
            for (int i = 0; i < data.Length; i++) data[i] ^= 0xAB;
            var dir = Path.GetDirectoryName(entry.OriginalPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.WriteAllBytes(entry.OriginalPath, data);
            File.Delete(entry.QuarantinePath);

            var list = LoadQuarantine();
            list.RemoveAll(e => e.Id == entry.Id);
            SaveQuarantine(list);
            return true;
        }
        catch { return false; }
    }

    public static bool DeleteQuarantineEntry(QuarantineEntry entry)
    {
        try
        {
            if (File.Exists(entry.QuarantinePath)) File.Delete(entry.QuarantinePath);
            var list = LoadQuarantine();
            list.RemoveAll(e => e.Id == entry.Id);
            SaveQuarantine(list);
            return true;
        }
        catch { return false; }
    }

    //  -- ? -- ? Histórico  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private static string HistoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Escritorio", "Vacinaldo", "history.json");

    public static List<ScanHistoryEntry> LoadHistory()
    {
        if (!File.Exists(HistoryPath)) return [];
        try
        {
            var json = File.ReadAllText(HistoryPath);
            return JsonSerializer.Deserialize<List<ScanHistoryEntry>>(json) ?? [];
        }
        catch { return []; }
    }

    public static void AppendHistory(ScanHistoryEntry entry)
    {
        var dir = Path.GetDirectoryName(HistoryPath)!;
        Directory.CreateDirectory(dir);
        var list = LoadHistory();
        list.Insert(0, entry);
        if (list.Count > 100) list = list.Take(100).ToList();
        File.WriteAllText(HistoryPath,
            JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void ClearHistory()
    {
        if (File.Exists(HistoryPath)) File.Delete(HistoryPath);
    }

    //  -- ? -- ? Proteção em tempo real  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private FileSystemWatcher? _watcher;
    public event Action<ThreatInfo>? ThreatDetected;

    /// <summary>Indica se a proteção em tempo real está ativa no momento.</summary>
    public bool IsRealTimeActive => _watcher is { EnableRaisingEvents: true };

    public void StartRealTimeProtection()
    {
        StopRealTimeProtection();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _watcher = new FileSystemWatcher(userProfile)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        _watcher.Created += OnFileCreated;
    }

    public void StopRealTimeProtection()
    {
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileCreated;
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        Thread.Sleep(300); // deixa o arquivo fechar
        var threat = InspectFile(e.FullPath);
        if (threat is not null)
            ThreatDetected?.Invoke(threat);
    }

    //  -- ? -- ? Utilitários  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();
            IEnumerable<string> files = [];
            try { files = Directory.EnumerateFiles(dir); } catch { }
            foreach (var f in files) yield return f;
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                    queue.Enqueue(sub);
            }
            catch { }
        }
    }

    public static string ThreatLevelLabel(ThreatLevel level) => level switch
    {
        ThreatLevel.Critical => "Crítica",
        ThreatLevel.High     => "Alta",
        ThreatLevel.Medium   => "Média",
        ThreatLevel.Low      => "Baixa",
        _                    => "Desconhecida",
    };

    public static string ThreatLevelColor(ThreatLevel level) => level switch
    {
        ThreatLevel.Critical => "#B71C1C",
        ThreatLevel.High     => "#E53935",
        ThreatLevel.Medium   => "#FB8C00",
        ThreatLevel.Low      => "#FDD835",
        _                    => "#9E9E9E",
    };
}

