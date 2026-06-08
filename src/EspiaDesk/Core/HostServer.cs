using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace EspiaDesk.Core;

public class HostServer
{
    private TcpListener? _listener;
    private readonly List<ClientSession> _clients = [];
    private readonly object _lock = new();
    private CancellationTokenSource _cts = new();

    public int Port          { get; set; } = 7070;
    public string PwdHash    { get; set; } = "";
    public int Quality       { get; set; } = 60;
    public int Fps           { get; set; } = 20;
    public bool Running      { get; private set; }

    public Func<string, string, Task<bool>>? OnAcceptRequest;
    public Action<ClientSession>? OnConnected;
    public Action<ClientSession>? OnDisconnected;
    public Action<string, string>? OnChat;

    public IReadOnlyList<ClientSession> Clients { get { lock (_lock) return [.._clients]; } }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        Running = true;
        _ = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcp = await _listener!.AcceptTcpClientAsync(ct);
                var session = new ClientSession(tcp, this);
                lock (_lock) _clients.Add(session);
                OnConnected?.Invoke(session);
                _ = session.RunAsync(ct).ContinueWith(_ =>
                {
                    lock (_lock) _clients.Remove(session);
                    OnDisconnected?.Invoke(session);
                });
            }
            catch when (ct.IsCancellationRequested) { break; }
            catch { }
        }
        Running = false;
    }

    public void Stop()
    {
        _cts.Cancel();
        lock (_lock) foreach (var c in _clients) c.Disconnect();
        _listener?.Stop();
        Running = false;
    }

    public string GetLocalIp()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            s.Connect("8.8.8.8", 80);
            return ((IPEndPoint)s.LocalEndPoint!).Address.ToString();
        }
        catch { return "127.0.0.1"; }
    }
}

public class ClientSession(TcpClient tcp, HostServer host)
{
    private readonly SessionCrypto _crypto = new();
    private NetworkStream? _stream;

    public string Id          { get; } = SessionCrypto.GenerateSessionId();
    public string RemoteName  { get; private set; } = "";
    public string RemoteIp    { get; } = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address.ToString();
    public DateTime StartTime { get; } = DateTime.Now;
    public bool Active        { get; private set; }
    public SessionInfo Info   => new() { Id = Id, RemoteName = RemoteName, RemoteIp = RemoteIp };

    public event Action<string, string>? ChatReceived;

    public async Task RunAsync(CancellationToken ct)
    {
        _stream = tcp.GetStream();
        try
        {
            if (!await HandshakeAsync()) return;
            Active = true;
            await Task.WhenAll(StreamScreenAsync(ct), ReceiveLoopAsync(ct));
        }
        finally { Active = false; tcp.Close(); }
    }

    private async Task<bool> HandshakeAsync()
    {
        await Protocol.SendAsync(_stream!, MsgType.AuthReq, _crypto.GetPublicKeyBytes());

        var (t1, clientPub) = await Protocol.ReceiveAsync(_stream!);
        if (t1 != MsgType.AuthReq) return false;

        var encKey = _crypto.EstablishAsServer(clientPub);
        await Protocol.SendAsync(_stream!, MsgType.AuthResp, encKey);

        var (t2, encCreds) = await Protocol.ReceiveAsync(_stream!);
        if (t2 != MsgType.AuthReq) return false;

        var creds = JsonSerializer.Deserialize<AuthCreds>(_crypto.Decrypt(encCreds))!;
        RemoteName = creds.Name ?? "Cliente";

        if (!string.IsNullOrEmpty(host.PwdHash) &&
            !SessionCrypto.VerifyPassword(creds.Password ?? "", host.PwdHash))
        {
            await SendAuthResult(false, "Senha incorreta");
            return false;
        }

        bool ok = host.OnAcceptRequest is null || await host.OnAcceptRequest(RemoteName, RemoteIp);
        if (!ok) { await SendAuthResult(false, "Recusado pelo host"); return false; }

        var (sw, sh) = ScreenCapture.GetPrimaryScreenSize();
        var result = _crypto.Encrypt(JsonSerializer.SerializeToUtf8Bytes(new AuthResult(
            true, null, Id, sw, sh, System.Net.Dns.GetHostName())));
        await Protocol.SendAsync(_stream!, MsgType.AuthResp, result);
        return true;
    }

    private async Task SendAuthResult(bool ok, string reason)
    {
        var data = _crypto.IsReady
            ? _crypto.Encrypt(JsonSerializer.SerializeToUtf8Bytes(new AuthResult(ok, reason, null, 0, 0, null)))
            : JsonSerializer.SerializeToUtf8Bytes(new AuthResult(ok, reason, null, 0, 0, null));
        await Protocol.SendAsync(_stream!, MsgType.AuthResp, data);
    }

    private async Task StreamScreenAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1.0 / host.Fps);
        while (Active && !ct.IsCancellationRequested)
        {
            try
            {
                var frame = ScreenCapture.CaptureJpeg(host.Quality);
                await Protocol.SendAsync(_stream!, MsgType.ScreenFrame, _crypto.Encrypt(frame));
            }
            catch { break; }
            await Task.Delay(delay, ct);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var (sw, sh) = ScreenCapture.GetPrimaryScreenSize();
        while (Active && !ct.IsCancellationRequested)
        {
            try
            {
                var (type, raw) = await Protocol.ReceiveAsync(_stream!);
                var data = _crypto.Decrypt(raw);

                switch (type)
                {
                    case MsgType.MouseMove:
                    { var e = Protocol.ParseJson<MouseEvent>(data)!; InputController.MoveMouse(e.X, e.Y, sw, sh); break; }

                    case MsgType.MouseClick:
                    { var e = Protocol.ParseJson<MouseEvent>(data)!; InputController.Click(e.X, e.Y, e.Btn, e.Dbl, sw, sh); break; }

                    case MsgType.MouseScroll:
                    { var e = Protocol.ParseJson<MouseEvent>(data)!; InputController.Scroll(e.Delta); break; }

                    case MsgType.KeyEvent:
                    {
                        var k = Protocol.ParseJson<KeyEvent>(data)!;
                        var vk = InputController.MapKey(k.Key);
                        if (vk != 0)
                        {
                            if (k.Action == "down")  InputController.KeyDown(vk);
                            else if (k.Action == "up") InputController.KeyUp(vk);
                            else InputController.KeyPress(vk);
                        }
                        break;
                    }

                    case MsgType.Chat:
                    {
                        var m = Protocol.ParseJson<ChatMsg>(data)!;
                        ChatReceived?.Invoke(m.Name, m.Msg);
                        host.OnChat?.Invoke(m.Name, m.Msg);
                        break;
                    }

                    case MsgType.Clipboard:
                    {
                        var c = Protocol.ParseJson<ClipboardMsg>(data)!;
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                            () => { try { System.Windows.Clipboard.SetText(c.Text); } catch { } });
                        break;
                    }

                    case MsgType.Control:
                    {
                        var c = Protocol.ParseJson<ControlMsg>(data)!;
                        if (c.Action == "disconnect") return;
                        break;
                    }
                }
            }
            catch { break; }
        }
    }

    public async Task SendChatAsync(string msg, string sender = "Host")
    {
        if (_stream is null || !Active) return;
        var data = _crypto.Encrypt(JsonSerializer.SerializeToUtf8Bytes(new ChatMsg(sender, msg)));
        await Protocol.SendAsync(_stream, MsgType.Chat, data);
    }

    public async Task SendClipboardAsync(string text)
    {
        if (_stream is null || !Active) return;
        var data = _crypto.Encrypt(JsonSerializer.SerializeToUtf8Bytes(new ClipboardMsg(text)));
        await Protocol.SendAsync(_stream, MsgType.Clipboard, data);
    }

    public void Disconnect()
    {
        Active = false;
        try { tcp.Close(); } catch { }
    }
}
