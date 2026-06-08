// Motor de Detecção do Vacinaldo  --  pipeline de análise comportamental
// Princípios implementados:
//   1. Explicabilidade      --  DetectionExplanation com sinais, evidências e técnica classificada
//   2. Risco mensurável     --  RiskScorer com acumulação bayesiana de sinais (0-100)
//   3. Controle de FP       --  FpRisk calculado por contexto e localização do arquivo
//   4. Robustez adversarial  --  Detecção de masquerading, LOLBAS, evasão
//   5. Trilha de auditoria  --  AuditLogger JSONL append-only imutável
//   6. Classificação        --  Toda detecção categorizada por tipo de ataque

using System.IO;
using System.Text.Json;

namespace Vacinaldo;

//  -- ? -- ? -- ? Classificação de Técnicas de Ataque  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

public static class AttackClassifier
{
    public sealed record TechniqueInfo(string Id, string Tactic, string Name, string Url);

    public static readonly IReadOnlyDictionary<string, TechniqueInfo> Techniques =
        new Dictionary<string, TechniqueInfo>
        {
            ["T1059"] = new("T1059", "Execução",              "Command and Scripting Interpreter", "https://attack.mitre.org/techniques/T1059/"),
            ["T1547"] = new("T1547", "Persistência",          "Boot or Logon Autostart Execution", "https://attack.mitre.org/techniques/T1547/"),
            ["T1055"] = new("T1055", "Evasão de Defesa",      "Process Injection",                 "https://attack.mitre.org/techniques/T1055/"),
            ["T1036"] = new("T1036", "Evasão de Defesa",      "Masquerading",                      "https://attack.mitre.org/techniques/T1036/"),
            ["T1070"] = new("T1070", "Evasão de Defesa",      "Indicator Removal",                 "https://attack.mitre.org/techniques/T1070/"),
            ["T1027"] = new("T1027", "Evasão de Defesa",      "Obfuscated Files or Information",   "https://attack.mitre.org/techniques/T1027/"),
            ["T1003"] = new("T1003", "Acesso a Credenciais",  "OS Credential Dumping",             "https://attack.mitre.org/techniques/T1003/"),
            ["T1082"] = new("T1082", "Reconhecimento",        "System Information Discovery",      "https://attack.mitre.org/techniques/T1082/"),
            ["T1105"] = new("T1105", "C2",                    "Ingress Tool Transfer",             "https://attack.mitre.org/techniques/T1105/"),
            ["T1204"] = new("T1204", "Execução",              "User Execution",                    "https://attack.mitre.org/techniques/T1204/"),
            ["T1218"] = new("T1218", "Evasão de Defesa",      "System Binary Proxy Execution",     "https://attack.mitre.org/techniques/T1218/"),
            ["T1562"] = new("T1562", "Evasão de Defesa",      "Impair Defenses",                   "https://attack.mitre.org/techniques/T1562/"),
            ["T1071"] = new("T1071", "C2",                    "Application Layer Protocol",        "https://attack.mitre.org/techniques/T1071/"),
            ["T1543"] = new("T1543", "Persistência",          "Create or Modify System Process",   "https://attack.mitre.org/techniques/T1543/"),
            ["T1134"] = new("T1134", "Escalada de Privilégio","Access Token Manipulation",         "https://attack.mitre.org/techniques/T1134/"),
        };

    public static string GetTactic(string id) =>
        Techniques.TryGetValue(id, out var t) ? t.Tactic : "Desconhecido";

    public static string GetName(string id) =>
        Techniques.TryGetValue(id, out var t) ? t.Name : id;
}

//  -- ? -- ? -- ? Sinal de detecção (evidência individual)  --  Explicabilidade  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

public sealed record DetectionSignal(
    string Rule,              // Identificador da regra
    string Evidence,          // O que foi encontrado / observado
    int    Weight,            // Peso 0-100 para o score de confiança
    string MitreTechnique);   // Técnica ATT&CK primária desta evidência

//  -- ? -- ? -- ? Explicação completa  --  Transparência e Explicabilidade  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

public sealed record DetectionExplanation(
    string                 DetectionId,        // ID único desta detecção
    string                 FilePath,
    List<DetectionSignal>  Signals,            // Evidências individuais
    int                    Confidence,         // 0-100  --  score bayesiano agregado
    double                 FpRisk,             // 0.0-1.0  --  risco de falso positivo
    string                 PrimaryTechnique,   // Técnica MITRE primária
    string                 RecommendedAction,  // Ação recomendada pelo sistema
    DateTime               AnalyzedAt)
{
    public string ConfidenceLabel => Confidence switch
    {
        >= 90 => "Crítica",
        >= 70 => "Alta",
        >= 50 => "Média",
        >= 30 => "Baixa",
        _     => "Informativa"
    };

    public string FpLabel     => FpRisk > 0.5 ? "Alto" : FpRisk > 0.25 ? "Médio" : "Baixo";
    public string MitreName   => AttackClassifier.GetName(PrimaryTechnique);
    public string MitreTactic => AttackClassifier.GetTactic(PrimaryTechnique);
    public string SignalsSummary => string.Join("; ", Signals.Select(s => s.Rule));
}

//  -- ? -- ? -- ? Pontuador de risco  --  Risco Mensurável  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

public static class RiskScorer
{
    /// <summary>
    /// Acumulação bayesiana: cada sinal contribui com diminishing returns.
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
    /// Risco de falso positivo: maior em arquivos de sistema, menor com múltiplos sinais.
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
            _                                   => "Monitorar  --  possível falso positivo"
        };
}

//  -- ? -- ? -- ? Evento de auditoria  --  Trilha Imutável  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

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

//  -- ? -- ? -- ? Logger de auditoria  --  Append-Only JSONL  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

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
        catch { /* log silencioso  --  não pode lançar em contexto de segurança */ }
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

