using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SamuraiGBA.Library;

namespace SamuraiGBA;

public sealed class CheatsWindow : Window
{
    readonly string id;
    readonly ObservableCollection<Cheat> cheats;
    readonly ListBox list = new();
    readonly TextBox name = new(), code = new();
    public List<string> ActiveCodes { get; private set; } = new();

    public CheatsWindow(string gameId, string title)
    {
        Theme.Chrome(this);
        id = gameId;
        cheats = new ObservableCollection<Cheat>(CheatStore.Load(gameId));
        Title = "Cheats — " + GameEntry.Clean(title);
        Width = 560; Height = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["Bg"];
        Foreground = Brushes.White;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        list.ItemsSource = cheats;
        list.Background = (Brush)Application.Current.Resources["Panel"];
        list.BorderThickness = new Thickness(0);
        var factory = new FrameworkElementFactory(typeof(CheckBox));
        factory.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("Name"));
        factory.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new System.Windows.Data.Binding("Enabled") { Mode = System.Windows.Data.BindingMode.TwoWay });
        factory.SetValue(Control.ForegroundProperty, Brushes.White);
        factory.SetValue(Control.PaddingProperty, new Thickness(6, 3, 0, 3));
        list.ItemTemplate = new DataTemplate { VisualTree = factory };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is Cheat c) { name.Text = c.Name; code.Text = c.Code; }
        };
        Grid.SetRow(list, 0);
        grid.Children.Add(list);

        var form = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        form.Children.Add(new TextBlock { Text = "Name", Foreground = (Brush)Application.Current.Resources["Muted"] });
        form.Children.Add(name);
        form.Children.Add(new TextBlock { Text = "Code(s) — one per line (GameShark / Action Replay / CodeBreaker, e.g. 82003A00 0063)", Margin = new Thickness(0, 8, 0, 0), Foreground = (Brush)Application.Current.Resources["Muted"], TextWrapping = TextWrapping.Wrap });
        code.AcceptsReturn = true; code.Height = 90; code.FontFamily = new FontFamily("Consolas");
        code.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        form.Children.Add(code);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(Btn("Add", (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(code.Text)) return;
            cheats.Add(new Cheat { Name = string.IsNullOrWhiteSpace(name.Text) ? "Cheat " + (cheats.Count + 1) : name.Text.Trim(), Code = code.Text.Trim(), Enabled = true });
        }));
        row.Children.Add(Btn("Update selected", (_, _) =>
        {
            int i = list.SelectedIndex; if (i < 0) return;
            cheats[i] = new Cheat { Name = name.Text.Trim(), Code = code.Text.Trim(), Enabled = cheats[i].Enabled };
        }));
        row.Children.Add(Btn("Remove", (_, _) => { if (list.SelectedItem is Cheat c) cheats.Remove(c); }));
        var spacer = new Border { Width = 40 };
        row.Children.Add(spacer);
        row.Children.Add(Btn("Apply && Close", (_, _) =>
        {
            CheatStore.Save(id, cheats);
            ActiveCodes = cheats.Where(c => c.Enabled).Select(c => c.Code).ToList();
            DialogResult = true;
        }));
        form.Children.Add(row);
        Grid.SetRow(form, 1);
        grid.Children.Add(form);
        Content = grid;
    }

    static Button Btn(string text, RoutedEventHandler click)
    {
        var b = new Button { Content = text.Replace("&&", "&") };
        b.Click += click;
        return b;
    }
}
