using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SamuraiGBA.Core;

namespace SamuraiGBA;

public sealed class ControlsWindow : Window
{
    static readonly Dictionary<int, string> PadNames = new()
    {
        [0x1] = "D-Pad Up", [0x2] = "D-Pad Down", [0x4] = "D-Pad Left", [0x8] = "D-Pad Right",
        [0x10] = "Start", [0x20] = "Back", [0x40] = "L-Stick Click", [0x80] = "R-Stick Click",
        [0x100] = "LB", [0x200] = "RB", [0x1000] = "A", [0x2000] = "B", [0x4000] = "X", [0x8000] = "Y",
    };

    readonly Settings S = Settings.Current;
    readonly Dictionary<string, string> keys;
    readonly Dictionary<string, int> pad;
    string fastKey;
    string? capturing;
    ushort lastPad;
    readonly Dictionary<string, TextBlock> labels = new();
    readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(30) };
    readonly TextBlock hint = new() { Margin = new Thickness(0, 10, 0, 0), Foreground = (Brush)Application.Current.Resources["Gold"], TextWrapping = TextWrapping.Wrap };

    public ControlsWindow()
    {
        Theme.Chrome(this);
        keys = new(S.Keys); pad = new(S.Pad); fastKey = S.FastForwardKey;
        Title = "Controls"; Width = 520; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["Bg"]; Foreground = Brushes.White;

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = "Click Rebind, then press a keyboard key or a gamepad button (Xbox / XInput). Esc cancels.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["Muted"], Margin = new Thickness(0, 0, 0, 10) });

        foreach (var (n, _) in Settings.Buttons) root.Children.Add(Row(n, n));
        root.Children.Add(Row("Fast-forward (hold)", "FastForward"));
        root.Children.Add(hint);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var reset = new Button { Content = "Defaults", Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x33)) };
        reset.Click += (_, _) => { keys.Clear(); foreach (var kv in Settings.DefaultKeys()) keys[kv.Key] = kv.Value; pad.Clear(); foreach (var kv in Settings.DefaultPad()) pad[kv.Key] = kv.Value; fastKey = "Tab"; RefreshAll(); };
        var save = new Button { Content = "Save" };
        save.Click += (_, _) =>
        {
            S.Keys = keys; S.Pad = pad; S.FastForwardKey = fastKey; S.Save(); InputState.Rebuild(); Close();
        };
        bar.Children.Add(reset); bar.Children.Add(save);
        root.Children.Add(bar);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        RefreshAll();

        PreviewKeyDown += OnKey;
        poll.Tick += (_, _) => PollPad();
        poll.Start();
        Closed += (_, _) => poll.Stop();
    }

    Grid Row(string label, string id)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        var val = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["Gold"] };
        Grid.SetColumn(val, 1); g.Children.Add(val); labels[id] = val;
        var b = new Button { Content = "Rebind", Padding = new Thickness(10, 4, 10, 4), Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x33)) };
        b.Click += (_, _) => { capturing = id; lastPad = InputState.ReadPadNow(); hint.Text = $"Press a key or gamepad button for “{label}”…"; };
        Grid.SetColumn(b, 2); g.Children.Add(b);
        return g;
    }

    void RefreshAll()
    {
        foreach (var (n, _) in Settings.Buttons)
        {
            string p = pad.TryGetValue(n, out var m) && PadNames.TryGetValue(m, out var pn) ? pn : "—";
            labels[n].Text = $"Key: {keys.GetValueOrDefault(n, "—")}    Pad: {p}";
        }
        labels["FastForward"].Text = $"Key: {fastKey}";
        hint.Text = "";
    }

    void OnKey(object s, KeyEventArgs e)
    {
        if (capturing == null) return;
        e.Handled = true;
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        if (k != Key.Escape)
        {
            if (capturing == "FastForward") fastKey = k.ToString(); else keys[capturing] = k.ToString();
        }
        capturing = null; RefreshAll();
    }

    void PollPad()
    {
        var now = InputState.ReadPadNow();
        var fresh = (ushort)(now & ~lastPad);
        lastPad = now;
        if (capturing == null || capturing == "FastForward" || fresh == 0) return;
        int mask = fresh & -fresh;
        pad[capturing] = mask;
        capturing = null; RefreshAll();
    }
}
