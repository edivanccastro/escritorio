using System.Runtime.InteropServices;

namespace EspiaDesk.Core;

public static class InputController
{
    // ── P/Invoke structs ──────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)] struct INPUT    { public uint Type; public UNION U; }
    [StructLayout(LayoutKind.Explicit)]   struct UNION    { [FieldOffset(0)] public MI Mi; [FieldOffset(0)] public KI Ki; }
    [StructLayout(LayoutKind.Sequential)] struct MI       { public int Dx, Dy; public uint Data, Flags, Time; public nint Extra; }
    [StructLayout(LayoutKind.Sequential)] struct KI       { public ushort Vk, Scan; public uint Flags, Time; public nint Extra; }

    [DllImport("user32.dll")] static extern uint  SendInput(uint n, INPUT[] inp, int sz);
    [DllImport("user32.dll")] static extern bool  SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern short GetKeyState(int vk);

    const uint MOUSE   = 0, KB      = 1;
    const uint LDOWN   = 0x02, LUP   = 0x04;
    const uint RDOWN   = 0x08, RUP   = 0x10;
    const uint MDOWN   = 0x20, MUP   = 0x40;
    const uint WHEEL   = 0x800;
    const uint KEYUP   = 0x02;

    // ── Mouse ─────────────────────────────────────────────────────────────────
    public static void MoveMouse(double relX, double relY, int sw, int sh)
        => SetCursorPos((int)(relX * sw), (int)(relY * sh));

    public static void Click(double relX, double relY, int btn, bool dbl, int sw, int sh)
    {
        SetCursorPos((int)(relX * sw), (int)(relY * sh));
        var (down, up) = btn switch { 2 => (MDOWN, MUP), 3 => (RDOWN, RUP), _ => (LDOWN, LUP) };
        int times = dbl ? 2 : 1;
        for (int i = 0; i < times; i++) { Mouse(down); Mouse(up); }
    }

    public static void Scroll(int delta)
        => SendInput(1, [new INPUT { Type = MOUSE, U = new UNION { Mi = new MI
            { Flags = WHEEL, Data = unchecked((uint)(delta * 120)) } } }],
            Marshal.SizeOf<INPUT>());

    // ── Keyboard ──────────────────────────────────────────────────────────────
    public static void KeyDown(ushort vk) => Key(vk, 0);
    public static void KeyUp(ushort vk)   => Key(vk, KEYUP);
    public static void KeyPress(ushort vk) { KeyDown(vk); KeyUp(vk); }

    public static ushort MapKey(string key) => key.ToUpperInvariant() switch
    {
        "ENTER" or "RETURN"  => 0x0D, "ESC" or "ESCAPE"  => 0x1B,
        "SPACE"               => 0x20, "BACKSPACE"         => 0x08,
        "TAB"                 => 0x09, "DELETE"            => 0x2E,
        "INSERT"              => 0x2D, "HOME"              => 0x24,
        "END"                 => 0x23, "PAGEUP"            => 0x21,
        "PAGEDOWN"            => 0x22, "UP"                => 0x26,
        "DOWN"                => 0x28, "LEFT"              => 0x25,
        "RIGHT"               => 0x27, "CTRL"              => 0x11,
        "ALT"                 => 0x12, "SHIFT"             => 0x10,
        "WIN"                 => 0x5B, "CAPSLOCK"          => 0x14,
        "F1"  => 0x70, "F2"  => 0x71, "F3"  => 0x72, "F4"  => 0x73,
        "F5"  => 0x74, "F6"  => 0x75, "F7"  => 0x76, "F8"  => 0x77,
        "F9"  => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
        _ when key.Length == 1 => (ushort)char.ToUpper(key[0]),
        _ => 0
    };

    // ── Helpers ───────────────────────────────────────────────────────────────
    static void Mouse(uint flags) =>
        SendInput(1, [new INPUT { Type = MOUSE, U = new UNION { Mi = new MI { Flags = flags } } }],
                  Marshal.SizeOf<INPUT>());

    static void Key(ushort vk, uint flags) =>
        SendInput(1, [new INPUT { Type = KB, U = new UNION { Ki = new KI { Vk = vk, Flags = flags } } }],
                  Marshal.SizeOf<INPUT>());
}
