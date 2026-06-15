using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DustBowlGames.DiscordConduit.App.Services;
using DustBowlGames.DiscordConduit.App.Views;
using DustBowlGames.DiscordConduit.App.ViewModels;
using DustBowlGames.DiscordConduit.Core.Migration;
using Serilog;

namespace DustBowlGames.DiscordConduit.App;

public partial class App : Application
{
    private AppServices? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiscordConduit");
        Directory.CreateDirectory(appDataPath);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(appDataPath, "logs", "discord-conduit-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        // Clean up old migration state files
        MigrationState.CleanupOldStatesAsync(appDataPath, Log.Logger).GetAwaiter().GetResult();

        _services = new AppServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(_services)
            };

            // Tracks whether the async ShutdownRequested path already disposed the
            // services, so the synchronous Exit fallback doesn't double-dispose.
            var disposed = false;

            // ShutdownRequested fires before Exit and lets us run the async disconnect
            // off the UI synchronization context, avoiding a deadlock on exit.
            desktop.ShutdownRequested += (_, _) =>
            {
                if (_services is not null && !disposed)
                {
                    disposed = true;
                    Task.Run(async () => await _services.DisposeAsync()).GetAwaiter().GetResult();
                }
            };

            desktop.Exit += (_, _) =>
            {
                if (_services is not null && !disposed)
                {
                    disposed = true;
                    // Safe synchronous fallback (offloads off the UI context internally).
                    _services.Dispose();
                }
                Log.CloseAndFlush();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
