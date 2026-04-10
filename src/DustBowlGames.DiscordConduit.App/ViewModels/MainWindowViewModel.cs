using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DustBowlGames.DiscordConduit.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _currentView = "Profiles";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string? _statusMessage = "Select a bot profile to get started.";

    [ObservableProperty]
    private ProfileManagerViewModel _profileManager = new();

    [ObservableProperty]
    private ChannelBrowserViewModel _channelBrowser = new();

    [ObservableProperty]
    private MigrationPreviewViewModel _migrationPreview = new();

    [ObservableProperty]
    private MigrationProgressViewModel _migrationProgress = new();

    [ObservableProperty]
    private ObservableObject? _activeContent;

    public MainWindowViewModel()
    {
        ActiveContent = ProfileManager;
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
            "Profiles" => IsConnected ? "Connected. Manage bot profiles." : "Select a bot profile to get started.",
            "Browser" => "Select a source and destination channel.",
            "Preview" => "Review migration details before starting.",
            "Migration" => "Migration in progress.",
            _ => null
        };
    }
}
