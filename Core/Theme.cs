using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SamuraiGBA;

static class Theme
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    /// <summary>Dark title bar + app icon for any window.</summary>
    public static void Chrome(Window w)
    {
        try { w.Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")); } catch { }
        w.SourceInitialized += (_, _) =>
        {
            try { int on = 1; DwmSetWindowAttribute(new WindowInteropHelper(w).Handle, 20, ref on, 4); } catch { }
        };
    }
}
