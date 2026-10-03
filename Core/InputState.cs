using System.Runtime.InteropServices;
using System.Windows.Input;

namespace SamuraiGBA.Core;

/// <summary>Keyboard (fed by the window) + XInput gamepad state, readable from the emulation thread.</summary>
public static class InputState
{
    public static readonly bool[] Keys = new bool[256];

    [StructLayout(LayoutKind.Sequential)]
    struct XState { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }

    [DllImport("xinput1_4.dll")] static extern int XInputGetState(int user, out XState state);

    struct Bind { public int Key; public int Mask; }
    static Bind[] map = new Bind[12];
    static ushort pad; static short lx, ly;
    static bool xinputBroken;

    static InputState() => Rebuild();

    public static void Rebuild()
    {
        var s = Settings.Current;
        var m = new Bind[12];
        foreach (var (name, id) in Settings.Buttons)
        {
            int k = -1;
            if (s.Keys.TryGetValue(name, out var ks) && Enum.TryParse<Key>(ks, out var key) && (int)key < 256) k = (int)key;
            s.Pad.TryGetValue(name, out var mask);
            m[id] = new Bind { Key = k, Mask = mask };
        }
        map = m;
    }

    public static void ClearKeys() => Array.Clear(Keys);

    public static ushort ReadPadNow()
    {
        if (xinputBroken) return 0;
        try { return XInputGetState(0, out var st) == 0 ? st.Buttons : (ushort)0; }
        catch { xinputBroken = true; return 0; }
    }

    public static void Poll()
    {
        if (xinputBroken) return;
        try
        {
            if (XInputGetState(0, out var st) == 0) { pad = st.Buttons; lx = st.LX; ly = st.LY; }
            else { pad = 0; lx = ly = 0; }
        }
        catch { xinputBroken = true; pad = 0; }
    }

    public static bool IsDown(uint id)
    {
        if (id >= 12) return false;
        var b = map[id];
        if (b.Key >= 0 && Keys[b.Key]) return true;
        if (b.Mask != 0 && (pad & b.Mask) != 0) return true;
        const int dz = 16000;
        return id switch { 4 => ly > dz, 5 => ly < -dz, 6 => lx < -dz, 7 => lx > dz, _ => false };
    }
}
