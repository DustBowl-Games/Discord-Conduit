using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DustBowlGames.DiscordConduit.App.ViewModels;

public partial class ChannelBrowserViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<GuildViewModel> _guilds = [];

    [ObservableProperty]
    private ChannelNodeViewModel? _selectedSource;

    [ObservableProperty]
    private ChannelNodeViewModel? _selectedDestination;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task LoadGuildsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // TODO: Wire up GuildEndpoints.GetCurrentUserGuildsAsync
            // Then for each guild, get channels via ChannelEndpoints.GetGuildChannelsAsync
            // Build the tree structure
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load guilds: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SetAsSource(ChannelNodeViewModel? channel)
    {
        if (channel is null) return;
        SelectedSource = channel;
    }

    [RelayCommand]
    private void SetAsDestination(ChannelNodeViewModel? channel)
    {
        if (channel is null) return;
        SelectedDestination = channel;
    }

    public bool CanStartMigration => SelectedSource is not null && SelectedDestination is not null;
}

public partial class GuildViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _iconUrl;

    [ObservableProperty]
    private ObservableCollection<ChannelNodeViewModel> _channels = [];

    [ObservableProperty]
    private bool _isExpanded;
}

public partial class ChannelNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _guildId = string.Empty;

    [ObservableProperty]
    private string _guildName = string.Empty;

    [ObservableProperty]
    private int _type;

    [ObservableProperty]
    private ObservableCollection<ChannelNodeViewModel> _children = [];

    [ObservableProperty]
    private bool _isExpanded;

    public bool IsThread => Type is 10 or 11 or 12;
    public bool IsTextChannel => Type is 0 or 5;
    public bool IsCategory => Type == 4;

    public string DisplayPrefix => Type switch
    {
        4 => "",           // Category
        0 => "# ",         // Text channel
        5 => "📢 ",        // Announcement
        10 or 11 => "🧵 ", // Thread
        12 => "🔒 ",       // Private thread
        15 => "💬 ",       // Forum
        _ => "? "
    };

    public string FullDisplayName => $"{DisplayPrefix}{Name}";
}
