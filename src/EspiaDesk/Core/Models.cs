namespace EspiaDesk.Core;

// ── Rede ──────────────────────────────────────────────────────────────────────
record AuthCreds(string? Name, string? Password);
record AuthResult(bool Ok, string? Reason, string? SessionId,
                  int ScreenWidth, int ScreenHeight, string? HostName);
record MouseEvent(double X, double Y, int Btn = 1, bool Dbl = false, int Delta = 0);
record KeyEvent(string Key, string Action);
record ChatMsg(string Name, string Msg);
record ClipboardMsg(string Text);
record ControlMsg(string Action);

// ── Sessão ────────────────────────────────────────────────────────────────────
public record SessionDetails(string SessionId, int ScreenWidth, int ScreenHeight,
                              string HostName, string RemoteIp);

public class SessionInfo
{
    public string Id { get; init; } = "";
    public string RemoteName { get; init; } = "";
    public string RemoteIp { get; init; } = "";
    public DateTime StartTime { get; } = DateTime.Now;
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public int FramesSent { get; set; }

    public string Duration => (DateTime.Now - StartTime) switch
    {
        var d when d.TotalHours >= 1 => $"{(int)d.TotalHours:D2}:{d.Minutes:D2}:{d.Seconds:D2}",
        var d => $"{d.Minutes:D2}:{d.Seconds:D2}"
    };
}
