using System.IO;
using System.IO.Pipes;
using System.Windows;
using Microsoft.Win32;

namespace Vacinaldo;

public partial class App : System.Windows.Application
{
    // â”€â”€ Constantes de instÃ¢ncia Ãºnica â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private const string MutexName = "Vacinaldo_SingleInstance_{7A9D4C1E-2F3B-4E8A-B6D2-93C5F0E1A847}";
    private const string PipeName  = "VacinaldoIPC";

    // â”€â”€ Estado â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private Mutex?       _mutex;
    private TrayManager? _tray;
    private CancellationTokenSource _pipeCts = new();

    // â”€â”€ Startup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Verifica instÃ¢ncia Ãºnica
        _mutex = new Mutex(true, MutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // JÃ¡ existe uma instÃ¢ncia rodando â€” sinaliza para ela mostrar a janela
            TrySignalExistingInstance();
            Shutdown();
            return;
        }

        // A partir daqui somos a instÃ¢ncia primÃ¡ria
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Inicia servidor IPC para receber sinal de instÃ¢ncias futuras
        _ = RunIpcServerAsync(_pipeCts.Token);

        // Registra no inÃ­cio do Windows (uma vez)
        RegisterStartup();

        // Cria bandeja e inicia proteÃ§Ã£o em segundo plano â€” SEM abrir janela
        _tray = new TrayManager(this);
        _tray.Initialize();
    }

    // â”€â”€ Shutdown â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts.Cancel();
        _tray?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // â”€â”€ IPC: servidor (instÃ¢ncia primÃ¡ria) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async Task RunIpcServerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server);
                var msg = await reader.ReadLineAsync(ct);
                if (msg == "SHOW")
                    _tray?.ShowWindow();
            }
            catch (OperationCanceledException) { break; }
            catch { /* pipe resetado â€” aguarda nova conexÃ£o */ }
        }
    }

    // â”€â”€ IPC: cliente (instÃ¢ncias subsequentes) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static void TrySignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 1500);   // espera atÃ© 1,5 s
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine("SHOW");
        }
        catch { }
    }

    // â”€â”€ Registro de inicializaÃ§Ã£o com o Windows â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static void RegisterStartup()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;

            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;

            // SÃ³ registra se ainda nÃ£o estiver lÃ¡ (ou o caminho mudou)
            var current = key.GetValue("Vacinaldo")?.ToString();
            if (current != exePath)
                key.SetValue("Vacinaldo", exePath);
        }
        catch { }
    }
}

