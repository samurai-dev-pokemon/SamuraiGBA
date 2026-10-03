namespace SamuraiGBA.Core;

public static class AppPaths
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SamuraiGBA");
    public static readonly string Saves = Path.Combine(Root, "saves");
    public static readonly string States = Path.Combine(Root, "states");
    public static readonly string Covers = Path.Combine(Root, "covers");
    public static readonly string Cheats = Path.Combine(Root, "cheats");
    public static readonly string Core = Path.Combine(Root, "core");
    public static readonly string Shots = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Samurai GBA");

    static AppPaths()
    {
        foreach (var d in new[] { Root, Saves, States, Covers, Cheats, Core, Shots })
            Directory.CreateDirectory(d);
    }
}
