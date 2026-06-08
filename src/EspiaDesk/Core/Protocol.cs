using System.IO;
using System.Net.Sockets;
using System.Text.Json;

namespace EspiaDesk.Core;

public enum MsgType : byte
{
    ScreenFrame = 0x01,
    MouseMove   = 0x02,
    MouseClick  = 0x03,
    MouseScroll = 0x04,
    KeyEvent    = 0x05,
    Chat        = 0x06,
    FileStart   = 0x07,
    FileChunk   = 0x08,
    FileEnd     = 0x09,
    Audio       = 0x0A,
    Clipboard   = 0x0B,
    AuthReq     = 0x0C,
    AuthResp    = 0x0D,
    Control     = 0x0E,
    Heartbeat   = 0x11,
}

public static class Protocol
{
    public static byte[] Pack(MsgType type, byte[] payload)
    {
        var buf = new byte[5 + payload.Length];
        buf[0] = (byte)type;
        var len = BitConverter.GetBytes(payload.Length);
        if (BitConverter.IsLittleEndian) Array.Reverse(len);
        len.CopyTo(buf, 1);
        payload.CopyTo(buf, 5);
        return buf;
    }

    public static byte[] PackJson<T>(MsgType type, T data)
        => Pack(type, JsonSerializer.SerializeToUtf8Bytes(data));

    public static async Task SendAsync(NetworkStream s, MsgType type, byte[] payload)
        => await s.WriteAsync(Pack(type, payload));

    public static async Task SendJsonAsync<T>(NetworkStream s, MsgType type, T data)
        => await SendAsync(s, type, JsonSerializer.SerializeToUtf8Bytes(data));

    public static async Task<(MsgType Type, byte[] Payload)> ReceiveAsync(NetworkStream s)
    {
        var header = new byte[5];
        await ReadExactAsync(s, header, 5);
        var type = (MsgType)header[0];
        var lenBytes = header[1..5];
        if (BitConverter.IsLittleEndian) Array.Reverse(lenBytes);
        var length = BitConverter.ToInt32(lenBytes);
        var payload = length > 0 ? new byte[length] : [];
        if (length > 0) await ReadExactAsync(s, payload, length);
        return (type, payload);
    }

    private static async Task ReadExactAsync(NetworkStream s, byte[] buf, int count)
    {
        int received = 0;
        while (received < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(received, count - received));
            if (n == 0) throw new IOException("Conexão encerrada pelo host remoto");
            received += n;
        }
    }

    public static T? ParseJson<T>(byte[] payload)
        => JsonSerializer.Deserialize<T>(payload);
}
