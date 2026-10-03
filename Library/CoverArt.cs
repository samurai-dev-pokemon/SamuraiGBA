using System.Security.Cryptography;
using System.Text;
using SamuraiGBA.Core;

namespace SamuraiGBA.Library;

/// <summary>
/// Cover art: (1) image next to the ROM with the same name, (2) libretro-thumbnails boxart
/// (thumbnails.libretro.com, keyed by No-Intro names), (3) title-screen image, (4) generated placeholder.
/// </summary>
public static class CoverArt
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".webp" };
    const string System_ = "Nintendo - Game Boy Advance";

    static CoverArt() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("SamuraiGBA/1.0");

    static string CachePath(GameEntry g)
    {
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(g.RomPath.ToLowerInvariant())));
        return Path.Combine(AppPaths.Covers, hash + ".png");
    }

    static string Sanitize(string n)
    {
        foreach (var c in "&*/:`<>?\\|") n = n.Replace(c, '_');
        return n;
    }

    public static bool UseLocal(GameEntry g)
    {
        var dir = Path.GetDirectoryName(g.RomPath)!;
        var name = Path.GetFileNameWithoutExtension(g.RomPath);
        foreach (var e in Exts)
        {
            var p = Path.Combine(dir, name + e);
            if (File.Exists(p)) { g.CoverPath = p; return true; }
        }
        return false;
    }

    public static async Task<bool> FetchAsync(GameEntry g)
    {
        if (g.HasCover) return true;
        if (UseLocal(g)) return true;

        var cache = CachePath(g);
        if (File.Exists(cache)) { g.CoverPath = cache; return true; }

        var baseName = Path.GetFileNameWithoutExtension(g.RomPath);
        var names = new List<string> { baseName };
        var stem = baseName.Contains(" (") ? baseName[..baseName.IndexOf(" (", StringComparison.Ordinal)] : baseName;
        foreach (var region in new[] { "(USA)", "(Europe)", "(Japan)", "(USA, Europe)", "(World)" })
            names.Add($"{stem} {region}");

        foreach (var type in new[] { "Named_Boxarts", "Named_Titles" })
            foreach (var n in names.Distinct())
            {
                var url = $"https://thumbnails.libretro.com/{Uri.EscapeDataString(System_)}/{type}/{Uri.EscapeDataString(Sanitize(n))}.png";
                try
                {
                    using var r = await Http.GetAsync(url);
                    if (!r.IsSuccessStatusCode) continue;
                    await File.WriteAllBytesAsync(cache, await r.Content.ReadAsByteArrayAsync());
                    g.CoverPath = cache;
                    return true;
                }
                catch { return false; } // offline: stop trying this game
            }
        return false;
    }

    public static void SetCustom(GameEntry g, string file)
    {
        var dest = Path.Combine(AppPaths.Covers, Path.GetFileNameWithoutExtension(CachePath(g)) + "_custom" + Path.GetExtension(file));
        File.Copy(file, dest, true);
        g.CoverPath = dest;
    }
}
