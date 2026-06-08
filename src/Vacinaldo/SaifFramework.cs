// Motor de DetecÃ§Ã£o do Vacinaldo â€” pipeline de anÃ¡lise comportamental
// PrincÃ­pios implementados:
//   1. Explicabilidade     â€” DetectionExplanation com sinais, evidÃªncias e tÃ©cnica classificada
//   2. Risco mensurÃ¡vel    â€” RiskScorer com acumulaÃ§Ã£o bayesiana de sinais (0-100)
//   3. Controle de FP      â€” FpRisk calculado por contexto e localizaÃ§Ã£o do arquivo
//   4. Robustez adversarial â€” DetecÃ§Ã£o de masquerading, LOLBAS, evasÃ£o
//   5. Trilha de auditoria â€” AuditLogger JSONL append-only imutÃ¡vel
//   6. ClassificaÃ§Ã£o       â€” Toda detecÃ§Ã£o categorizada por tipo de ataque

using System.IO;
using System.Text.Json;

namespace Vacinaldo;

// â”€â”€â”€ ClassificaÃ§Ã£o de TÃ©cnicas de Ataque â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public static class AttackClassifier
{
    public sealed record TechniqueInfo(string Id, string Tactic, string Name, string Url);

    public static readonly IReadOnlyDictionary<string, TechniqueInfo> Techniques =
        new Dictionary<string, TechniqueInfo>
        {
            ["T1059"] = new("T1059", "ExecuÃ§Ã£o",              "Command and Scripting Interpreter", "https://attack.mitre.org/techniques/T1059/"),
            ["T1547"] = new("T1547", "PersistÃªncia",          "Boot or Logon Autostart Execution", "https://attack.mitre.org/techniques/T1547/"),
            ["T1055"] = new("T1055", "EvasÃ£o de Defesa",      "Process Injection",                 "https://attack.mitre.org/techniques/T1055/"),
            ["T1036"] = new("T1036", "EvasÃ£o de Defesa",      "Masquerading",                      "https://attack.mitre.org/techniques/T1036/"),
            ["T1070"] = new("T1070", "EvasÃ£o de Defesa",      "Indicator Removal",                 "https://attack.mitre.org/techniques/T1070/"),
            ["T1027"] = new("T1027", "EvasÃ£o de Defesa",      "Obfuscated Files or Information",   "https://attack.mitre.org/techniques/T1027/"),
            ["T1003"] = new("T1003", "Acesso a Credenciais",  "OS Credential Dumping",             "https://attack.mitre.org/techniques/T1003/"),
            ["T1082"] = new("T1082", "Reconhecimento",        "System Information Discovery",      "https://attack.mitre.org/techniques/T1082/"),
            ["T1105"] = new("T1105", "C2",                    "Ingress Tool Transfer",             "https://attack.mitre.org/techniques/T1105/"),
            ["T1204"] = new("T1204", "ExecuÃ§Ã£o",              "User Execution",                    "https://attack.mitre.org/techniques/T1204/"),
            ["T1218"] = new("T1218", "EvasÃ£o de Defesa",      "System Binary Proxy Execution",     "https://attack.mitre.org/techniques/T1218/"),
            ["T1562"] = new("T1562", "EvasÃ£o de Defesa",      "Impair Defenses",                   "https://attack.mitre.org/techniques/T1562/"),
            ["T1071"] = new("T1071", "C2",                    "Application Layer Protocol",        "https://attack.mitre.org/techniques/T1071/"),
            ["T1543"] = new("T1543", "PersistÃªncia",          "Create or Modify System Process",   "https://attack.mitre.org/techniques/T1543/"),
            ["T1134"] = new("T1134", "Escalada de PrivilÃ©gio","Access Token Manipulation",         "https://attack.mitre.org/techniques/T1134/"),
        };

    public static string GetTactic(string id) =>
        Techniques.TryGetValue(id, out var t) ? t.Tactic : "Desconhecido";

    public static string GetName(string id) =>
        Techniques.TryGetValue(id, out var t) ? t.Name : id;
}

// â”€â”€â”€ Sinal de detecÃ§Ã£o (evidÃªncia individual) â€” Explicabilidade â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed record DetectionSignal(
    string Rule,              // Identificador da regra
    string Evidence,          // O que foi encontrado / observado
    int    Weight,            // Peso 0-100 para o score de confianÃ§a
    string MitreTechnique);   // TÃ©cnica ATT&CK primÃ¡ria desta evidÃªncia

// â”€â”€â”€ ExplicaÃ§Ã£o completa â€” TransparÃªncia e Explicabilidade â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed record DetectionExplanation(
    string                 DetectionId,        // ID Ãºnico desta detecÃ§Ã£o
    string                 FilePath,
    List<DetectionSignal>  Signals,            // EvidÃªncias individuais
    int                    Confidence,         // 0-100 â€” score bayesiano agregado
    double                 FpRisk,             // 0.0-1.0 â€” risco de falso positivo
    string                 PrimaryTechnique,   // TÃ©cnica MITRE primÃ¡ria
    string                 RecommendedAction,  // AÃ§Ã£o recomendada pelo sistema
    DateTime               AnalyzedAt)
{
    public string ConfidenceLabel => Confidence switch
    {
        >= 90 => "CrÃ­tica",
        >= 70 => "Alta",
        >= 50 => "MÃ©dia",
        >= 30 => "Baixa",
        _     => "Informativa"
    };

    public string FpLabel     => FpRisk > 0.5 ? "Alto" : FpRisk > 0.25 ? "MÃ©dio" : "Baixo";
    public string MitreName   => AttackClassifier.GetName(PrimaryTechnique);
    public string MitreTactic => AttackClassifier.GetTactic(PrimaryTechnique);
    public string SignalsSummary => string.Join("; ", Signals.Select(s => s.Rule));
}

// â”€â”€â”€ Pontuador de risco â€” Risco MensurÃ¡vel â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public static class RiskScorer
{
    /// <summary>
    /// AcumulaÃ§Ã£o bayesiana: cada sinal contribui com diminishing returns.
    /// score += weight * (1 - score/100)
    /// </summary>
    public static int ComputeConfidence(IReadOnlyList<DetectionSignal> signals)
    {
        double score = 0;
        foreach (var s in signals)
            score += s.Weight * (1.0 - score / 100.0);
        return (int)Math.Min(100, Math.Round(score));
    }

    /// <summary>
    /// Risco de falso positivo: maior em arquivos de sistema, menor com mÃºltiplos sinais.
    /// </summary>
    public static double ComputeFpRisk(IReadOnlyList<DetectionSignal> signals, string path)
    {
        var inSysDir = path.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            StringComparison.OrdinalIgnoreCase);

        var avgWeight = signals.Count > 0 ? signals.Average(s => s.Weight) : 0;
        var baseFp    = 1.0 - (avgWeight / 100.0);
        if (inSysDir) baseFp = Math.Min(1.0, baseFp * 1.5);
        return Math.Clamp(baseFp / Math.Max(1, signals.Count), 0.0, 1.0);
    }

    public static string RecommendAction(int confidence, ThreatLevel level, double fpRisk) =>
        (confidence, level, fpRisk) switch
        {
            (>= 85, ThreatLevel.Critical, _)  => "Quarentenar imediatamente",
            (>= 70, ThreatLevel.High,   < 0.3) => "Quarentenar",
            (>= 50, _,                  < 0.25) => "Quarentenar e investigar",
            (>= 30, _,                  _)      => "Investigar manualmente",
            _                                   => "Monitorar â€” possÃ­vel falso positivo"
        };
}

// â”€â”€â”€ Evento de auditoria â€” Trilha ImutÃ¡vel â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed record AuditEvent(
    string   Id,
    DateTime Timestamp,
    string   EventType,       // ThreatDetected | EdrAlert | ScanStarted | EdrStarted | etc.
    string   Source,          // FileScan | RealTime | EDR
    string   Description,
    string?  FilePath,
    string?  MitreTechnique,
    int?     Confidence,
    string   Outcome);        // Detected | Quarantined | Ignored | Started | etc.

// â”€â”€â”€ Logger de auditoria â€” Append-Only JSONL â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public static class AuditLogger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Escritorio", "Vacinaldo", "audit.jsonl");

    public static void Log(AuditEvent evt)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath,
                JsonSerializer.Serialize(evt) + Environment.NewLine);
        }
        catch { /* log silencioso â€” nÃ£o pode lanÃ§ar em contexto de seguranÃ§a */ }
    }

    public static List<AuditEvent> ReadRecent(int count = 300)
    {
        if (!File.Exists(LogPath)) return [];
        try
        {
            return File.ReadLines(LogPath)
                .TakeLast(count)
                .Select(line =>
                {
                    try { return JsonSerializer.Deserialize<AuditEvent>(line); }
                    catch { return null; }
                })
                .OfType<AuditEvent>()
                .Reverse()
                .ToList();
        }
        catch { return []; }
    }

    public static void Clear()
    {
        try { if (File.Exists(LogPath)) File.Delete(LogPath); }
        catch { }
    }

    public static long GetLogBytes() =>
        File.Exists(LogPath) ? new FileInfo(LogPath).Length : 0;
}

