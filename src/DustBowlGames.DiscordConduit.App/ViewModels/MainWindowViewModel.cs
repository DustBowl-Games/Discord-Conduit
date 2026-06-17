using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustBowlGames.DiscordConduit.App.Services;

namespace DustBowlGames.DiscordConduit.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly AppServices _services;

    [ObservableProperty]
    private string _currentView = "Profiles";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string? _statusMessage = "Select a bot profile to get started.";

    public ProfileManagerViewModel ProfileManager { get; }
    public ChannelBrowserViewModel ChannelBrowser { get; }
    public MigrationPreviewViewModel MigrationPreview { get; }
    public MigrationProgressViewModel MigrationProgress { get; }

    [ObservableProperty]
    private ObservableObject? _activeContent;

    public MainWindowViewModel() : this(new AppServices()) { }

    public MainWindowViewModel(AppServices services)
    {
        _services = services;

        ProfileManager = new ProfileManagerViewModel(services, OnConnectionChanged);
        ChannelBrowser = new ChannelBrowserViewModel(services);
        MigrationPreview = new MigrationPreviewViewModel(
            services,
            () => ChannelBrowser.SelectedSource,
            () => ChannelBrowser.SelectedDestination);
        MigrationProgress = new MigrationProgressViewModel(
            services,
            () => MigrationPreview.CurrentOptions);

        ActiveContent = ProfileManager;

        // Load profiles on startup
        _ = ProfileManager.LoadProfilesCommand.ExecuteAsync(null);
    }

    private void OnConnectionChanged(bool connected)
    {
        IsConnected = connected;
        StatusMessage = connected
            ? "Connected. Browse channels to set up a migration."
            : "Select a bot profile to get started.";

        if (connected)
        {
            // Auto-load guilds when connected
            _ = ChannelBrowser.LoadGuildsCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void NavigateTo(string view)
    {
        CurrentView = view;
        ActiveContent = view switch
        {
            "Profiles" => ProfileManager,
            "Browser" => ChannelBrowser,
            "Preview" => MigrationPreview,
            "Migration" => MigrationProgress,
            _ => ProfileManager
        };

        StatusMessage = view switch
        {
            "Profiles" => IsConnected ? "Connected. Browse channels to set up a migration." : "Select a bot profile to get started.",
            "Browser" => "Select a source and destination channel.",
            "Preview" => "Review migration details before starting.",
            "Migration" => "Monitor migration progress.",
            _ => null
        };
    }
}
