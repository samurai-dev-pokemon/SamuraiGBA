using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using SamuraiGBA.Core;

namespace SamuraiGBA.Library;

public sealed class GameLibrary
{
    static readonly string[] RomExts = { ".gba", ".agb" };
    static readonly string FilePath = Path.Combine(AppPaths.Root, "library.json");

    public ObservableCollection<GameEntry> Games { get; } = new();
    public ICollectionView View { get; }

    public GameLibrary()
    {
        View = CollectionViewSource.GetDefaultView(Games);
        View.SortDescriptions.Add(new SortDescription(nameof(GameEntry.Title), ListSortDirection.Ascending));
        try
        {
            if (File.Exists(FilePath))
                foreach (var g in JsonSerializer.Deserialize<List<GameEntry>>(File.ReadAllText(FilePath)) ?? new())
                    if (File.Exists(g.RomPath)) Games.Add(g);
        }
        catch { }
    }

    public void Filter(string text)
        => View.Filter = string.IsNullOrWhiteSpace(text) ? null
            : o => ((GameEntry)o).Title.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase);

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Games.ToList())); } catch { }
    }

    public GameEntry? AddRom(string path)
    {
        if (!RomExts.Contains(Path.GetExtension(path).ToLowerInvariant())) return null;
        var existing = Games.FirstOrDefault(g => g.RomPath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;
        var e = new GameEntry { RomPath = path };
        CoverArt.UseLocal(e);
        Games.Add(e);
        Save();
        return e;
    }

    public async Task<int> AddFolderAsync(string dir)
    {
        var found = await Task.Run(() =>
            Directory.EnumerateFiles(dir, "*.*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                     .Where(f => RomExts.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList());
        int added = 0;
        foreach (var f in found)
            if (!Games.Any(g => g.RomPath.Equals(f, StringComparison.OrdinalIgnoreCase))) { AddRom(f); added++; }
        return added;
    }

    public void Remove(GameEntry g) { Games.Remove(g); Save(); }

    public async Task FetchAllCoversAsync()
    {
        var gate = new SemaphoreSlim(4);
        var tasks = Games.Where(g => !g.HasCover).ToList().Select(async g =>
        {
            await gate.WaitAsync();
            try { await CoverArt.FetchAsync(g); } finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
        Save();
    }
}
