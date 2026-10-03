using System.Text.Json;
using SamuraiGBA.Core;

namespace SamuraiGBA.Library;

public sealed class Cheat
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public static class CheatStore
{
    /// <summary>ID from the GBA header (game title, game code, maker, version) — no need to hash the whole ROM.</summary>
    public static string IdFor(string romPath)
    {
        try
        {
            using var fs = File.OpenRead(romPath);
            var h = new byte[0xC0];
            int n = fs.Read(h, 0, h.Length);
            if (n >= 0xBD)
            {
                string title = System.Text.Encoding.ASCII.GetString(h, 0xA0, 12).Trim('\0', ' ');
                string code = System.Text.Encoding.ASCII.GetString(h, 0xAC, 4);
                string maker = System.Text.Encoding.ASCII.GetString(h, 0xB0, 2);
                string id = $"{code}-{maker}-{h[0xBC]:X2}-{title}";
                foreach (var c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
                return id;
            }
        }
        catch { }
        return Path.GetFileNameWithoutExtension(romPath);
    }

    static string FileFor(string id) => Path.Combine(AppPaths.Cheats, id + ".json");

    public static List<Cheat> Load(string id)
    {
        try { if (File.Exists(FileFor(id))) return JsonSerializer.Deserialize<List<Cheat>>(File.ReadAllText(FileFor(id))) ?? new(); }
        catch { }
        return new();
    }

    public static void Save(string id, IEnumerable<Cheat> cheats)
        => File.WriteAllText(FileFor(id), JsonSerializer.Serialize(cheats.ToList(), new JsonSerializerOptions { WriteIndented = true }));
}
