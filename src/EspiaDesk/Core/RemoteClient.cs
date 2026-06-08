using System.IO;
using System.Net.Sockets;
using System.Text.Json;

namespace EspiaDesk.Core;

public class RemoteClient
{
    private TcpClient?    _tcp;
    private NetworkStream? _stream;
    private readonly SessionCrypto _crypto = new();

    public bool   Connected    { get; private set; }
    public int    RemoteWidth  { get; private set; } = 1920;
    public int    RemoteHeight { get; private set; } = 1080;
    public string LocalName    { get; set; } = System.Net.Dns.GetHostName();

    public event Action<byte[]>?         FrameReceived;
    public event Action<string, string>? ChatReceived;
    public event Action<string>?         ClipboardReceived;
    public event Action?                 Disconnected;

    public async Task<SessionDetails> ConnectAsync(string host, int port,
                                                   string password,
                                                   CancellationToken ct = default)
    {
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(host, port, ct);
        _stream = _tcp.GetStream();

        var details = await HandshakeAsync(host, password);
        RemoteWidth  = details.ScreenWidth;
        RemoteHeight = details.ScreenHeight;
        Connected = true;

        _ = ReceiveLoopAsync(ct);
        return details;
    }

    private async Task<SessionDetails> HandshakeAsync(string host, string password)
    {
        // 1. Receber chave pública do servidor
        var (t1, srvPub) = await Protocol.ReceiveAsync(_stream!);
        if (t1 != MsgType.AuthReq) throw new IOException("Handshake inválido");

        // 2. Enviar chave pública própria
        await Protocol.SendAsync(_stream!, MsgType.AuthReq, _crypto.GetPublicKeyBytes());

        // 3. Receber chave de sessão encriptada → estabelecer cifra
        var (t2, encKey) = await Protocol.ReceiveAsync(_stream!);
        if (t2 != MsgType.AuthResp) throw new IOException("Handshake inválido");
        _crypto.EstablishAsClient(encKey);

        // 4. Enviar credenciais encriptadas
        var creds = _crypto.Encrypt(
            JsonSerializer.SerializeToUtf8Bytes(new AuthCreds(LocalName, password)));
        await Protocol.SendAsync(_stream!, MsgType.AuthReq, creds);

        // 5. Receber resultado
        var (t3, encResult) = await Protocol.ReceiveAsync(_stream!);
        if (t3 != MsgType.AuthResp) throw new IOException("Handshake inválido");

        var result = JsonSerializer.Deserialize<AuthResult>(_crypto.Decrypt(encResult))!;
        if (!result.Ok)
            throw new UnauthorizedAccessException(result.Reason ?? "Acesso negado");

        return new SessionDetails(
            result.SessionId!, result.ScreenWidth, result.ScreenHeight,
            result.HostName ?? host, host);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (Connected && !ct.IsCancellationRequested)
        {
            try
            {
                var (type, raw) = await Protocol.ReceiveAsync(_stream!);
                var data = _crypto.Decrypt(raw);

                switch (type)
                {
                    case MsgType.ScreenFrame:
                        FrameReceived?.Invoke(data);
                        break;

                    case MsgType.Chat:
                    {
                        var m = Protocol.ParseJson<ChatMsg>(data)!;
                        ChatReceived?.Invoke(m.Name, m.Msg);
                        break;
                    }
                    case MsgType.Clipboard:
                    {
                        var c = Protocol.ParseJson<ClipboardMsg>(data)!;
                        ClipboardReceived?.Invoke(c.Text);
                        break;
                    }
                    case MsgType.Control:
                    {
                        var c = Protocol.ParseJson<ControlMsg>(data)!;
                        if (c.Action == "disconnect") goto done;
                        break;
                    }
                }
                continue;
                done: break;
            }
            catch { break; }
        }

        Connected = false;
        Disconnected?.Invoke();
    }

    private async Task SendEncAsync(MsgType type, object data)
    {
        if (_stream is null || !Connected) return;
        var bytes = _crypto.Encrypt(JsonSerializer.SerializeToUtf8Bytes(data));
        await Protocol.SendAsync(_stream, type, bytes);
    }

    public Task SendMouseMoveAsync(double rx, double ry)
        => SendEncAsync(MsgType.MouseMove, new MouseEvent(rx, ry));

    public Task SendMouseClickAsync(double rx, double ry, int btn = 1, bool dbl = false)
        => SendEncAsync(MsgType.MouseClick, new MouseEvent(rx, ry, btn, dbl));

    public Task SendMouseScrollAsync(double rx, double ry, int delta)
        => SendEncAsync(MsgType.MouseScroll, new MouseEvent(rx, ry, Delta: delta));

    public Task SendKeyEventAsync(string key, string action)
        => SendEncAsync(MsgType.KeyEvent, new KeyEvent(key, action));

    public Task SendChatAsync(string msg)
        => SendEncAsync(MsgType.Chat, new ChatMsg(LocalName, msg));

    public Task SendClipboardAsync(string text)
        => SendEncAsync(MsgType.Clipboard, new ClipboardMsg(text));

    public async Task DisconnectAsync()
    {
        Connected = false;
        try { await SendEncAsync(MsgType.Control, new ControlMsg("disconnect")); } catch { }
        _tcp?.Close();
        Disconnected?.Invoke();
    }
}
