using System.Text.Json;

namespace SamuraiGBA.Core;

public sealed class Settings
{
    // (name, libretro joypad id)
    public static readonly (string Name, uint Id)[] Buttons =
    {
        ("Up", 4), ("Down", 5), ("Left", 6), ("Right", 7),
        ("A", 8), ("B", 0), ("L", 10), ("R", 11), ("Start", 3), ("Select", 2),
    };

    public static Dictionary<string, string> DefaultKeys() => new()
    {
        ["Up"] = "Up", ["Down"] = "Down", ["Left"] = "Left", ["Right"] = "Right",
        ["A"] = "X", ["B"] = "Z", ["L"] = "A", ["R"] = "S", ["Start"] = "Return", ["Select"] = "Back",
    };

    // XInput button masks
    public static Dictionary<string, int> DefaultPad() => new()
    {
        ["Up"] = 0x0001, ["Down"] = 0x0002, ["Left"] = 0x0004, ["Right"] = 0x0008,
        ["A"] = 0x2000, ["B"] = 0x1000, ["L"] = 0x0100, ["R"] = 0x0200, ["Start"] = 0x0010, ["Select"] = 0x0020,
    };

    public Dictionary<string, string> Keys { get; set; } = DefaultKeys();
    public Dictionary<string, int> Pad { get; set; } = DefaultPad();
    public string FastForwardKey { get; set; } = "Tab";
    public float FastForwardSpeed { get; set; } = 3f;
    public string Filter { get; set; } = "Sharp";
    public bool IntegerScale { get; set; }
    public int Volume { get; set; } = 80;
    public bool Mute { get; set; }
    public List<string> Folders { get; set; } = new();

    static readonly string FilePath = Path.Combine(AppPaths.Root, "settings.json");
    public static Settings Current { get; private set; } = Load();

    static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                foreach (var kv in DefaultKeys()) s.Keys.TryAdd(kv.Key, kv.Value);
                foreach (var kv in DefaultPad()) s.Pad.TryAdd(kv.Key, kv.Value);
                return s;
            }
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }
}
