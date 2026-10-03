using System.Windows;

namespace SamuraiGBA;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, "Samurai GBA", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
