using System.IO;
using System.IO.Pipes;
using System.Windows;
using Microsoft.Win32;

namespace Vacinaldo;

public partial class App : System.Windows.Application
{
    //  -- ? -- ? Constantes de instância única  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private const string MutexName = "Vacinaldo_SingleInstance_{7A9D4C1E-2F3B-4E8A-B6D2-93C5F0E1A847}";
    private const string PipeName  = "VacinaldoIPC";

    //  -- ? -- ? Estado  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private Mutex?       _mutex;
    private TrayManager? _tray;
    private CancellationTokenSource _pipeCts = new();

    //  -- ? -- ? Startup  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Verifica instância única
        _mutex = new Mutex(true, MutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // Já existe uma instância rodando  --  sinaliza para ela mostrar a janela
            TrySignalExistingInstance();
            Shutdown();
            return;
        }

        // A partir daqui somos a instância primária
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Inicia servidor IPC para receber sinal de instâncias futuras
        _ = RunIpcServerAsync(_pipeCts.Token);

        // Registra no início do Windows (uma vez)
        RegisterStartup();

        // Cria bandeja e inicia proteção em segundo plano  --  SEM abrir janela
        _tray = new TrayManager(this);
        _tray.Initialize();
    }

    //  -- ? -- ? Shutdown  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts.Cancel();
        _tray?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    //  -- ? -- ? IPC: servidor (instância primária)  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

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
            catch { /* pipe resetado  --  aguarda nova conexão */ }
        }
    }

    //  -- ? -- ? IPC: cliente (instâncias subsequentes)  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private static void TrySignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 1500);   // espera até 1,5 s
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine("SHOW");
        }
        catch { }
    }

    //  -- ? -- ? Registro de inicialização com o Windows  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private static void RegisterStartup()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;

            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;

            // Só registra se ainda não estiver lá (ou o caminho mudou)
            var current = key.GetValue("Vacinaldo")?.ToString();
            if (current != exePath)
                key.SetValue("Vacinaldo", exePath);
        }
        catch { }
    }
}

