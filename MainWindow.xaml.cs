using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SamuraiGBA.Core;
using SamuraiGBA.Library;

namespace SamuraiGBA;

public partial class MainWindow : Window
{
    readonly Settings S = Settings.Current;
    readonly GameLibrary lib = new();
    readonly Emulator emu = new();
    readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };

    WriteableBitmap? bmp;
    int[] srcBuf = new int[240 * 160], outBuf = Array.Empty<int>();
    int fw = 240, fh = 160;
    string? currentRom, cheatId;
    bool fullscreen; WindowState prevState; WindowStyle prevStyle;

    public MainWindow()
    {
        InitializeComponent();
        Theme.Chrome(this);
        GamesList.ItemsSource = lib.View;
        lib.Games.CollectionChanged += (_, _) => RefreshHints();

        BuildMenus();
        RefreshHints();

        emu.Message += m => Dispatcher.BeginInvoke(() =>
        {
            if (m.StartsWith("Emulator error")) MessageBox.Show(this, m, "Samurai GBA", MessageBoxButton.OK, MessageBoxImage.Warning);
            else ShowToast(m);
        });
        emu.Stopped += () => Dispatcher.BeginInvoke(() => { if (!emu.Running) ShowGame(false); });
        toastTimer.Tick += (_, _) => { Toast.Visibility = Visibility.Collapsed; toastTimer.Stop(); };

        CompositionTarget.Rendering += RenderFrame;
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        Deactivated += (_, _) => InputState.ClearKeys();
        Drop += OnDrop;
        Closing += (_, _) => { emu.Stop(); S.Save(); lib.Save(); };
        Loaded += async (_, _) => await StartupScanAsync();
    }

    // ---------------------------------------------------------------- menus
    void BuildMenus()
    {
        foreach (var (key, label, _) in Filters.All)
        {
            var mi = new MenuItem { Header = label, Tag = key, IsCheckable = true, IsChecked = S.Filter == key };
            mi.Click += (_, _) =>
            {
                S.Filter = key; S.Save();
                foreach (MenuItem o in FilterMenu.Items) o.IsChecked = (string)o.Tag == key;
                bmp = null;
            };
            FilterMenu.Items.Add(mi);
        }
        IntegerItem.IsChecked = S.IntegerScale;
        MuteItem.IsChecked = S.Mute;

        for (int i = 1; i <= 5; i++)
        {
            int slot = i;
            var a = new MenuItem { Header = $"Slot {slot}" }; a.Click += (_, _) => { if (emu.Running) emu.SaveState(slot); };
            var b = new MenuItem { Header = $"Slot {slot}" }; b.Click += (_, _) => { if (emu.Running) emu.LoadState(slot); };
            SaveMenu.Items.Add(a); LoadMenu.Items.Add(b);
        }
        foreach (var v in new[] { 1.5f, 2f, 3f, 4f, 8f })
        {
            var mi = new MenuItem { Header = $"{v:0.#}x", IsCheckable = true, IsChecked = Math.Abs(S.FastForwardSpeed - v) < 0.01f };
            mi.Click += (_, _) =>
            {
                S.FastForwardSpeed = v; S.Save();
                foreach (MenuItem o in SpeedMenu.Items) o.IsChecked = o == mi;
            };
            SpeedMenu.Items.Add(mi);
        }
        foreach (var v in new[] { 25, 50, 75, 100 })
        {
            var mi = new MenuItem { Header = $"{v}%", IsCheckable = true, IsChecked = S.Volume == v };
            mi.Click += (_, _) =>
            {
                S.Volume = v; S.Save();
                foreach (MenuItem o in VolumeMenu.Items) o.IsChecked = o == mi;
                emu.Volume = v / 100f; emu.ApplyVolume();
            };
            VolumeMenu.Items.Add(mi);
        }
    }

    // ---------------------------------------------------------------- library
    void RefreshHints()
    {
        EmptyHint.Visibility = lib.Games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = $"{lib.Games.Count} game{(lib.Games.Count == 1 ? "" : "s")}";
        StatusText.Text = "Double-click a game to play  ·  right-click for cover art options";
    }

    async Task StartupScanAsync()
    {
        foreach (var d in S.Folders.ToList())
            if (Directory.Exists(d)) await lib.AddFolderAsync(d);
        await lib.FetchAllCoversAsync();
    }

    void Search_Changed(object s, TextChangedEventArgs e) => lib.Filter(SearchBox.Text);

    async void AddFolder_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose a folder containing GBA ROMs" };
        if (dlg.ShowDialog() != true) return;
        if (!S.Folders.Contains(dlg.FolderName)) { S.Folders.Add(dlg.FolderName); S.Save(); }
        StatusText.Text = "Scanning…";
        int n = await lib.AddFolderAsync(dlg.FolderName);
        RefreshHints();
        ShowToastOrStatus($"Added {n} game(s). Fetching cover art…");
        await lib.FetchAllCoversAsync();
        RefreshHints();
    }

    void AddRom_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "GBA ROMs (*.gba;*.agb)|*.gba;*.agb", Multiselect = true };
        if (dlg.ShowDialog() != true) return;
        foreach (var f in dlg.FileNames) lib.AddRom(f);
        _ = lib.FetchAllCoversAsync();
    }

    async void FetchCovers_Click(object s, RoutedEventArgs e)
    {
        ShowToastOrStatus("Fetching cover art…");
        await lib.FetchAllCoversAsync();
        RefreshHints();
    }

    GameEntry? Selected => GamesList.SelectedItem as GameEntry;
    void Games_DoubleClick(object s, MouseButtonEventArgs e) { if (Selected != null && e.OriginalSource is not ScrollViewer) Launch(Selected.RomPath); }
    void CtxPlay_Click(object s, RoutedEventArgs e) { if (Selected != null) Launch(Selected.RomPath); }
    async void CtxFetch_Click(object s, RoutedEventArgs e)
    {
        if (Selected is not { } g) return;
        bool ok = await CoverArt.FetchAsync(g);
        ShowToastOrStatus(ok ? "Cover art found." : "No online cover found — use 'Set custom cover…'. (Matching uses No-Intro style file names.)");
        lib.Save();
    }
    void CtxCover_Click(object s, RoutedEventArgs e)
    {
        if (Selected is not { } g) return;
        var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
        if (dlg.ShowDialog() == true) { CoverArt.SetCustom(g, dlg.FileName); lib.Save(); }
    }
    void CtxRemove_Click(object s, RoutedEventArgs e) { if (Selected is { } g) lib.Remove(g); }

    void ShowToastOrStatus(string m)
    {
        if (GameView.Visibility == Visibility.Visible) ShowToast(m); else StatusText.Text = m;
    }

    // ---------------------------------------------------------------- launching
    async Task<bool> EnsureCoreAsync()
    {
        if (CoreManager.Find() != null) return true;
        var r = MessageBox.Show(this,
            "Samurai GBA needs the mGBA emulation core (mgba_libretro.dll, MPL-2.0).\n\nDownload it now from the libretro build server?",
            "Emulation core", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return false;
        try { Title = "Samurai GBA — downloading core…"; await CoreManager.DownloadAsync(); Title = "Samurai GBA"; return true; }
        catch (Exception ex)
        {
            Title = "Samurai GBA";
            MessageBox.Show(this, "Download failed: " + ex.Message + "\n\nPlace mgba_libretro.dll next to SamuraiGBA.exe manually.",
                "Emulation core", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    async void Launch(string path)
    {
        if (!await EnsureCoreAsync()) return;
        var core = CoreManager.Find()!;
        currentRom = path;
        cheatId = CheatStore.IdFor(path);
        emu.Cheats = CheatStore.Load(cheatId).Where(c => c.Enabled).Select(c => c.Code).ToList();
        emu.Volume = S.Volume / 100f; emu.Mute = S.Mute;
        InputState.Rebuild(); InputState.ClearKeys();
        bmp = null;
        emu.Start(core, path);
        Title = "Samurai GBA — " + GameEntry.Clean(Path.GetFileNameWithoutExtension(path));
        ShowGame(true);
    }

    void ShowGame(bool on)
    {
        GameView.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        LibraryView.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (!on) { Title = "Samurai GBA"; if (fullscreen) ToggleFullscreen(); }
        else GameView.Focus();
    }

    void OnDrop(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        GameEntry? last = null;
        foreach (var f in files) last = lib.AddRom(f) ?? last;
        if (last != null) { _ = lib.FetchAllCoversAsync(); if (files.Length == 1) Launch(last.RomPath); }
    }

    // ---------------------------------------------------------------- rendering
    void RenderFrame(object? sender, EventArgs e)
    {
        if (GameView.Visibility != Visibility.Visible || !emu.Running) return;
        int w, h;
        lock (emu.FrameLock)
        {
            if (!emu.FrameReady) return;
            emu.FrameReady = false;
            w = emu.W; h = emu.H;
            if (srcBuf.Length != w * h) srcBuf = new int[w * h];
            Array.Copy(emu.Frame, srcBuf, w * h);
        }
        int f = Filters.FactorOf(S.Filter);
        if (bmp == null || bmp.PixelWidth != w * f || bmp.PixelHeight != h * f)
        {
            bmp = new WriteableBitmap(w * f, h * f, 96, 96, PixelFormats.Bgr32, null);
            outBuf = new int[w * f * h * f];
            Screen.Source = bmp;
            RenderOptions.SetBitmapScalingMode(Screen, S.Filter == "Nearest" ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Linear);
        }
        Filters.Apply(S.Filter, srcBuf, w, h, outBuf, f);
        bmp.WritePixels(new Int32Rect(0, 0, w * f, h * f), outBuf, w * f * 4, 0);
        if (fw != w || fh != h) { fw = w; fh = h; }
        UpdateScreenLayout();
    }

    void UpdateScreenLayout()
    {
        if (!S.IntegerScale)
        {
            Screen.Stretch = Stretch.Uniform; Screen.Width = double.NaN; Screen.Height = double.NaN;
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        double aw = GameView.ActualWidth * dpi.DpiScaleX, ah = GameView.ActualHeight * dpi.DpiScaleY;
        int k = Math.Max(1, (int)Math.Floor(Math.Min(aw / fw, ah / fh)));
        Screen.Stretch = Stretch.Fill;
        Screen.Width = fw * k / dpi.DpiScaleX; Screen.Height = fh * k / dpi.DpiScaleY;
    }

    void GameView_SizeChanged(object s, SizeChangedEventArgs e) => UpdateScreenLayout();

    void ShowToast(string m)
    {
        ToastText.Text = m; Toast.Visibility = Visibility.Visible;
        toastTimer.Stop(); toastTimer.Start();
    }

    // ---------------------------------------------------------------- input / hotkeys
    static Key Real(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

    void OnKeyDown(object s, KeyEventArgs e)
    {
        var k = Real(e);
        if ((int)k < 256) InputState.Keys[(int)k] = true;
        if (k.ToString() == S.FastForwardKey) emu.Speed = S.FastForwardSpeed;

        if (k == Key.O && Keyboard.Modifiers == ModifierKeys.Control) { OpenRom_Click(this, e); e.Handled = true; return; }
        if (GameView.Visibility != Visibility.Visible) return;

        switch (k)
        {
            case Key.Escape: if (fullscreen) ToggleFullscreen(); else Back_Click(this, e); e.Handled = true; return;
            case Key.P: emu.Paused = !emu.Paused; ShowToast(emu.Paused ? "Paused" : "Resumed"); e.Handled = true; return;
            case Key.F5: emu.SaveState(1); e.Handled = true; return;
            case Key.F8: emu.LoadState(1); e.Handled = true; return;
            case Key.F11: ToggleFullscreen(); e.Handled = true; return;
            case Key.F12: Shot_Click(this, e); e.Handled = true; return;
        }
        if (Keyboard.Modifiers == ModifierKeys.None) e.Handled = true; // keep arrows/Enter from driving the menu
    }

    void OnKeyUp(object s, KeyEventArgs e)
    {
        var k = Real(e);
        if ((int)k < 256) InputState.Keys[(int)k] = false;
        if (k.ToString() == S.FastForwardKey) emu.Speed = 1f;
    }

    void ToggleFullscreen()
    {
        if (!fullscreen)
        {
            prevState = WindowState; prevStyle = WindowStyle;
            WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized;
            MainMenu.Visibility = Visibility.Collapsed; fullscreen = true;
        }
        else
        {
            WindowStyle = prevStyle; WindowState = prevState;
            MainMenu.Visibility = Visibility.Visible; fullscreen = false;
        }
    }

    // ---------------------------------------------------------------- menu handlers
    void OpenRom_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "GBA ROMs (*.gba;*.agb)|*.gba;*.agb" };
        if (dlg.ShowDialog() != true) return;
        var g = lib.AddRom(dlg.FileName);
        _ = lib.FetchAllCoversAsync();
        Launch(dlg.FileName);
    }
    void Back_Click(object s, RoutedEventArgs e) { emu.Stop(); ShowGame(false); }
    void Exit_Click(object s, RoutedEventArgs e) => Close();
    void Pause_Click(object s, RoutedEventArgs e) { if (emu.Running) { emu.Paused = !emu.Paused; ShowToast(emu.Paused ? "Paused" : "Resumed"); } }
    void Reset_Click(object s, RoutedEventArgs e) { if (emu.Running) emu.Reset(); }
    void Integer_Click(object s, RoutedEventArgs e) { S.IntegerScale = IntegerItem.IsChecked; S.Save(); UpdateScreenLayout(); }
    void Fullscreen_Click(object s, RoutedEventArgs e) { if (GameView.Visibility == Visibility.Visible) ToggleFullscreen(); }
    void Mute_Click(object s, RoutedEventArgs e) { S.Mute = MuteItem.IsChecked; S.Save(); emu.Mute = S.Mute; emu.ApplyVolume(); }

    void Shot_Click(object s, RoutedEventArgs e)
    {
        if (!emu.Running) return;
        var src = BitmapSource.Create(fw, fh, 96, 96, PixelFormats.Bgr32, null, (int[])srcBuf.Clone(), fw * 4);
        var scaled = new TransformedBitmap(src, new ScaleTransform(3, 3));
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(scaled));
        var file = Path.Combine(AppPaths.Shots, $"{Path.GetFileNameWithoutExtension(currentRom)}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        using (var fs = File.Create(file)) enc.Save(fs);
        ShowToast("Screenshot saved to Pictures\\Samurai GBA");
    }

    void Cheats_Click(object s, RoutedEventArgs e)
    {
        if (currentRom == null || cheatId == null || !emu.Running)
        {
            MessageBox.Show(this, "Start a game first — cheats are stored per game.", "Cheats"); return;
        }
        var w = new CheatsWindow(cheatId, Path.GetFileNameWithoutExtension(currentRom)) { Owner = this };
        if (w.ShowDialog() == true) { emu.SetCheats(w.ActiveCodes); ShowToast($"{w.ActiveCodes.Count} cheat(s) active"); }
    }

    void Controls_Click(object s, RoutedEventArgs e) => new ControlsWindow { Owner = this }.ShowDialog();

    void Hotkeys_Click(object s, RoutedEventArgs e) => MessageBox.Show(this,
        "Esc – back to library (exits fullscreen first)\nP – pause\nHold fast-forward key (default Tab) – turbo\nF5 / F8 – quick save / quick load (slot 1)\nF11 – fullscreen\nF12 – screenshot\nCtrl+O – open ROM\n\nGame buttons and the gamepad are rebindable under Controls.",
        "Hotkeys");

    void About_Click(object s, RoutedEventArgs e) => MessageBox.Show(this,
        "Samurai GBA 1.0\nA Game Boy Advance front end built on the mGBA core (MPL-2.0).\nCover art courtesy of the libretro-thumbnails project.\n\nOnly play games you own.",
        "About");
}
