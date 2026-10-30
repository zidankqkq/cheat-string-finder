using System.Windows;
using CheatStringFinder.App.ViewModels;

namespace CheatStringFinder.App;

public partial class MainWindow : Window
{
    FinderViewModel Vm => (FinderViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) =>
        {
            bool max = WindowState == WindowState.Maximized;
            Root.BorderThickness = max ? new Thickness(0) : new Thickness(1);
            Root.Padding = max ? new Thickness(7) : new Thickness(0);
            MaxButton.Content = max ? "" : "";
        };
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && !Vm.IsBusy ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths && System.IO.File.Exists(paths[0])) Vm.CheatFile = paths[0];
    }

    /// <summary>Ctrl+C copies the selected strings (one per line) instead of WPF's cell copy.</summary>
    void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.C && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0 &&
            System.Windows.Input.Keyboard.FocusedElement is not System.Windows.Controls.TextBox &&
            Vm.CopySelectedCommand.CanExecute(StringsGrid.SelectedItems))
        {
            Vm.CopySelectedCommand.Execute(StringsGrid.SelectedItems);
            e.Handled = true;
        }
    }
}
