// EDR  --  Endpoint Detection and Response
// Monitora processos, conexões de rede e alterações de registro em tempo real.
// Cada evento é classificado pelo framework SAIF com técnica MITRE ATT&CK.

using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Vacinaldo;

//  -- ? -- ? -- ? Modelos de evento  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

public enum SecurityEventType
{
    ProcessSuspicious,
    NetworkSuspicious,
    RegistryPersistence,
    FileSuspicious
}

public enum EdrRisk { Info, Low, Medium, High, Critical }

public sealed record SecurityEvent(
    string            Id,
    DateTime          Timestamp,
    SecurityEventType Type,
    EdrRisk           Risk,
    string            Source,
    string            Description,
    string?           ProcessName,
    int?              ProcessId,
    string?           FilePath,
    string?           RemoteAddress,
    string?           RegistryKey,
    string?           MitreTechnique,
    string?           MitreTactic);

public sealed record ProcessSnapshot(
    int      Pid,
    string   Name,
    string   Path,
    int      MemoryMB,
    int      Threads,
    EdrRisk  Risk,
    string?  MitreTechnique,
    string?  RiskReason);

public sealed record NetworkConnection(
    string  LocalAddr,
    int     LocalPort,
    string  RemoteAddr,
    int     RemotePort,
    string  State,
    bool    IsSuspicious,
    string? SuspiciousReason);

//  -- ? -- ? -- ? Motor EDR  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

public sealed class EdrEngine : IDisposable
{
    // Evento disparado quando um alerta de nível Medium+ é emitido
    public event Action<SecurityEvent>? AlertRaised;

    private readonly List<SecurityEvent> _timeline = [];
    private readonly object              _lock     = new();
    private CancellationTokenSource?     _cts;
    private bool                         _running;

    public bool IsRunning => _running;

    public IReadOnlyList<SecurityEvent> Timeline
    {
        get { lock (_lock) return _timeline.TakeLast(500).ToList(); }
    }

    //  -- ? -- ? Ciclo de vida  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    public void Start()
    {
        if (_running) return;
        _running = true;
        _cts     = new CancellationTokenSource();

        _ = MonitorLoopAsync(_cts.Token);
        _ = MonitorRegistryAsync(_cts.Token);

        AuditLogger.Log(new AuditEvent(
            Guid.NewGuid().ToString("N")[..8], DateTime.Now,
            "EdrStarted", "EDR",
            "Motor EDR iniciado  --  monitoramento comportamental ativo.",
            null, null, null, "Started"));
    }

    public void Stop()
    {
        _running = false;
        _cts?.Cancel();
        _cts = null;
    }

    public void Dispose() => Stop();

    //  -- ? -- ? Snapshot de processos (chamada sob demanda pela UI)  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    public List<ProcessSnapshot> GetProcessSnapshot()
    {
        var result = new List<ProcessSnapshot>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var path = GetProcessPath(p);
                var (risk, tech, reason) = ScoreProcess(p.ProcessName, path);
                result.Add(new ProcessSnapshot(
                    Pid:           p.Id,
                    Name:          p.ProcessName,
                    Path:          path,
                    MemoryMB:      (int)(p.WorkingSet64 / 1_048_576L),
                    Threads:       p.Threads.Count,
                    Risk:          risk,
                    MitreTechnique: tech,
                    RiskReason:    reason));
            }
            catch { }
        }
        return result.OrderByDescending(p => (int)p.Risk).ThenBy(p => p.Name).ToList();
    }

    //  -- ? -- ? Conexões de rede (chamada sob demanda pela UI)  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    public List<NetworkConnection> GetNetworkConnections()
    {
        var result = new List<NetworkConnection>();
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            foreach (var c in props.GetActiveTcpConnections())
            {
                var remAddr = c.RemoteEndPoint.Address.ToString();
                var remPort = c.RemoteEndPoint.Port;
                var (susp, reason) = ScoreConnection(remAddr, remPort);
                result.Add(new NetworkConnection(
                    LocalAddr:        c.LocalEndPoint.Address.ToString(),
                    LocalPort:        c.LocalEndPoint.Port,
                    RemoteAddr:       remAddr,
                    RemotePort:       remPort,
                    State:            c.State.ToString(),
                    IsSuspicious:     susp,
                    SuspiciousReason: reason));
            }
        }
        catch { }
        return result.OrderByDescending(c => c.IsSuspicious).ToList();
    }

    //  -- ? -- ? Loop principal de monitoramento  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private HashSet<int> _seenPids = [];

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                CheckProcesses();
                CheckConnections();
            }
            catch { }
            await Task.Delay(8_000, ct).ConfigureAwait(false);
        }
    }

    private void CheckProcesses()
    {
        var current = new HashSet<int>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                current.Add(p.Id);
                if (_seenPids.Contains(p.Id)) continue;

                var path = GetProcessPath(p);
                var (risk, tech, reason) = ScoreProcess(p.ProcessName, path);
                if (risk < EdrRisk.Medium) continue;

                Emit(new SecurityEvent(
                    Id:             Guid.NewGuid().ToString("N")[..8],
                    Timestamp:      DateTime.Now,
                    Type:           SecurityEventType.ProcessSuspicious,
                    Risk:           risk,
                    Source:         "ProcessMonitor",
                    Description:    $"Processo suspeito: {p.ProcessName} (PID {p.Id})  --  {reason}",
                    ProcessName:    p.ProcessName,
                    ProcessId:      p.Id,
                    FilePath:       path,
                    RemoteAddress:  null,
                    RegistryKey:    null,
                    MitreTechnique: tech,
                    MitreTactic:    tech is not null ? AttackClassifier.GetTactic(tech) : null));
            }
            catch { }
        }
        _seenPids = current;
    }

    // Mantém track de conexões suspeitas já reportadas para evitar spam
    private readonly HashSet<string> _seenConns = [];

    private void CheckConnections()
    {
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            foreach (var c in props.GetActiveTcpConnections())
            {
                var remote = $"{c.RemoteEndPoint.Address}:{c.RemoteEndPoint.Port}";
                if (_seenConns.Contains(remote)) continue;
                _seenConns.Add(remote);

                var (susp, reason) = ScoreConnection(
                    c.RemoteEndPoint.Address.ToString(), c.RemoteEndPoint.Port);
                if (!susp) continue;

                Emit(new SecurityEvent(
                    Id:             Guid.NewGuid().ToString("N")[..8],
                    Timestamp:      DateTime.Now,
                    Type:           SecurityEventType.NetworkSuspicious,
                    Risk:           EdrRisk.Medium,
                    Source:         "NetworkMonitor",
                    Description:    $"Conexão suspeita para {remote}: {reason}",
                    ProcessName:    null,
                    ProcessId:      null,
                    FilePath:       null,
                    RemoteAddress:  remote,
                    RegistryKey:    null,
                    MitreTechnique: "T1071",
                    MitreTactic:    AttackClassifier.GetTactic("T1071")));
            }
        }
        catch { }
    }

    //  -- ? -- ? Monitoramento de registro (entradas de inicialização)  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private async Task MonitorRegistryAsync(CancellationToken ct)
    {
        var baseline = SnapshotRunKeys();
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(30_000, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) break;

            var current = SnapshotRunKeys();
            foreach (var kv in current)
            {
                if (baseline.TryGetValue(kv.Key, out var old) && old == kv.Value) continue;

                Emit(new SecurityEvent(
                    Id:             Guid.NewGuid().ToString("N")[..8],
                    Timestamp:      DateTime.Now,
                    Type:           SecurityEventType.RegistryPersistence,
                    Risk:           EdrRisk.High,
                    Source:         "RegistryMonitor",
                    Description:    $"Entrada de inicialização nova/modificada: {kv.Key}",
                    ProcessName:    null,
                    ProcessId:      null,
                    FilePath:       null,
                    RemoteAddress:  null,
                    RegistryKey:    kv.Key,
                    MitreTechnique: "T1547",
                    MitreTactic:    AttackClassifier.GetTactic("T1547")));
            }
            baseline = current;
        }
    }

    private static Dictionary<string, string> SnapshotRunKeys()
    {
        var result = new Dictionary<string, string>();
        ReadRunHive(Registry.CurrentUser,  result);
        ReadRunHive(Registry.LocalMachine, result);
        return result;
    }

    private static void ReadRunHive(RegistryKey hive, Dictionary<string, string> into)
    {
        const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
                into[$"{hive.Name}\\{path}\\{name}"] = key.GetValue(name)?.ToString() ?? "";
        }
        catch { }
    }

    //  -- ? -- ? Pontuação de processos  --  SAIF: Robustez Adversarial  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    // Living Off the Land Binaries (LOLBAS)  --  usados para proxy de execução
    private static readonly HashSet<string> LolbasBins = new(StringComparer.OrdinalIgnoreCase)
    {
        "mshta","wscript","cscript","regsvr32","rundll32","certutil","bitsadmin",
        "msiexec","installutil","regasm","regsvcs","msbuild","cmstp","expand",
        "extrac32","findstr","hh","mavinject","odbcconf","pcalua","replace",
        "rpcping","schtasks","secedit","vbc","verclsid","wmic","wsreset","wuauclt",
        "forfiles","syncappvpublishingserver","appsyncpublishingserver","dnscmd",
    };

    // Processos do sistema legítimos  --  usados em masquerading quando fora de Windows
    private static readonly HashSet<string> SystemProcs = new(StringComparer.OrdinalIgnoreCase)
        { "svchost","lsass","winlogon","services","csrss","smss","wininit","explorer" };

    private static (EdrRisk risk, string? technique, string? reason) ScoreProcess(
        string name, string path)
    {
        // Masquerading: nome de sistema rodando fora de C:\Windows
        if (SystemProcs.Contains(name) &&
            !string.IsNullOrEmpty(path) &&
            !path.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase))
            return (EdrRisk.Critical, "T1036", $"Masquerading: '{name}' fora de C:\\Windows");

        // Executável em pasta temporária
        var tempBase = Path.GetTempPath();
        if (!string.IsNullOrEmpty(path) &&
            path.StartsWith(tempBase, StringComparison.OrdinalIgnoreCase))
            return (EdrRisk.High, "T1059", "Executável rodando de pasta temporária");

        // LOLBAS  --  proxy de execução do sistema
        if (LolbasBins.Contains(name))
            return (EdrRisk.Medium, "T1218", $"LOLBAS: '{name}' pode ser usado para proxy de execução");

        // Interpreters de script legítimos mas de risco potencial
        if (name is "powershell" or "pwsh" or "cmd")
            return (EdrRisk.Low, "T1059", $"Interpreter de script ativo: {name}");

        return (EdrRisk.Info, null, null);
    }

    //  -- ? -- ? Pontuação de conexões  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    // Portas frequentemente usadas por RATs, backdoors e C2 frameworks
    private static readonly HashSet<int> SuspPorts =
        [4444, 1337, 31337, 6666, 5555, 8888, 9999, 1234, 12345, 54321, 6667, 6697];

    private static (bool suspicious, string? reason) ScoreConnection(string addr, int port)
    {
        // Loopback e IPs não-roteáveis são seguros
        if (addr is "127.0.0.1" or "::1" or "0.0.0.0") return (false, null);
        if (addr.StartsWith("192.168.") || addr.StartsWith("10.") ||
            Regex.IsMatch(addr, @"^172\.(1[6-9]|2\d|3[01])\."))
            return (false, null);

        // Porta C2 conhecida
        if (SuspPorts.Contains(port))
            return (true, $"Porta {port}  --  frequentemente usada por RATs e C2 frameworks");

        // Porta não-padrão para tráfego de internet em IP público
        if (port is not (80 or 443 or 8080 or 8443 or 53 or 22 or 25 or 465 or 587 or 143 or 993))
            return (true, $"Conexão a IP público em porta não-convencional {port}");

        return (false, null);
    }

    //  -- ? -- ? Emissão de evento  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private void Emit(SecurityEvent evt)
    {
        lock (_lock)
        {
            _timeline.Add(evt);
            if (_timeline.Count > 1000) _timeline.RemoveAt(0);
        }

        AlertRaised?.Invoke(evt);

        AuditLogger.Log(new AuditEvent(
            evt.Id, evt.Timestamp, "EdrAlert", evt.Source,
            evt.Description, evt.FilePath, evt.MitreTechnique,
            null, "Detected"));
    }

    //  -- ? -- ? Utilidades  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private static string GetProcessPath(Process p)
    {
        try { return p.MainModule?.FileName ?? string.Empty; }
        catch { return string.Empty; }
    }
}

