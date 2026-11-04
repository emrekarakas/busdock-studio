using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using BusDockStudio.Desktop.ViewModels;

namespace BusDockStudio.Desktop.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void CopyBody_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(viewModel.SelectedBody);
        }
    }

    private async void CopyMetadata_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(viewModel.SelectedMetadata);
        }
    }
}
