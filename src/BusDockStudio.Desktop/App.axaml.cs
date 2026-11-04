using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using BusDockStudio.AzureServiceBus;
using BusDockStudio.Desktop.ViewModels;
using BusDockStudio.Desktop.Views;
using BusDockStudio.Infrastructure;

namespace BusDockStudio;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var secretStore = new InMemorySecretStore();
            var settings = new InMemoryApplicationSettings();
            var viewModel = new MainWindowViewModel(
                new AzureServiceBusConnectionService(),
                new AzureEntityBrowser(),
                new AzureMessageBrowser(),
                new AzureMessagePurgeService(),
                secretStore,
                settings,
                new SystemClock(),
                SetTheme);

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.Exit += (_, _) => viewModel.Dispose();
            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetTheme(string theme)
    {
        RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
