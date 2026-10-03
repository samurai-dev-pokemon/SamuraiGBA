using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SamuraiGBA.Library;

public sealed class GameEntry : INotifyPropertyChanged
{
    string? coverPath;
    ImageSource? cover;

    public string RomPath { get; set; } = "";

    public string? CoverPath
    {
        get => coverPath;
        set { coverPath = value; cover = null; Notify(nameof(Cover)); }
    }

    [JsonIgnore]
    public string Title => Clean(System.IO.Path.GetFileNameWithoutExtension(RomPath));

    [JsonIgnore]
    public bool HasCover => !string.IsNullOrEmpty(coverPath) && File.Exists(coverPath);

    [JsonIgnore]
    public ImageSource Cover => cover ??= Load();

    ImageSource Load()
    {
        if (HasCover)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = 360;
                bi.UriSource = new Uri(coverPath!);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { }
        }
        return Placeholder(Title);
    }

    public static string Clean(string name)
        => Regex.Replace(name, @"\s*[\(\[][^\)\]]*[\)\]]", "").Trim();

    static ImageSource Placeholder(string title)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var bg = new LinearGradientBrush(Color.FromRgb(0x9A, 0x0B, 0x22), Color.FromRgb(0x14, 0x14, 0x1A), 90);
            dc.DrawRectangle(bg, null, new Rect(0, 0, 320, 320));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), null, new Point(160, 110), 70, 70);
            var ft = new FormattedText(title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"), 28, Brushes.White, 1.0)
            { MaxTextWidth = 280, MaxTextHeight = 140, TextAlignment = TextAlignment.Center };
            dc.DrawText(ft, new Point(20, 190));
        }
        var rtb = new RenderTargetBitmap(320, 320, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
