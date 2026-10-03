using System.IO.Compression;

namespace SamuraiGBA.Core;

/// <summary>Locates (or downloads) the mGBA libretro core (mgba_libretro.dll, MPL-2.0).</summary>
public static class CoreManager
{
    const string Url = "https://buildbot.libretro.com/nightly/windows/x86_64/latest/mgba_libretro.dll.zip";
    const string Dll = "mgba_libretro.dll";

    public static string? Find()
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, AppPaths.Core })
        {
            var p = Path.Combine(dir, Dll);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static async Task DownloadAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SamuraiGBA/1.0");
        var bytes = await http.GetByteArrayAsync(Url);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var entry = zip.Entries.First(e => e.Name.Equals(Dll, StringComparison.OrdinalIgnoreCase));
        entry.ExtractToFile(Path.Combine(AppPaths.Core, Dll), overwrite: true);
    }
}
