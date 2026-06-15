using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustBowlGames.DiscordConduit.App.Services;
using DustBowlGames.DiscordConduit.Core.Api.Models;
using Serilog;

namespace DustBowlGames.DiscordConduit.App.ViewModels;

public partial class ChannelBrowserViewModel : ObservableObject
{
    private readonly AppServices _services;

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

    public ChannelBrowserViewModel() : this(null!) { }

    public ChannelBrowserViewModel(AppServices services)
    {
        _services = services;
    }

    [RelayCommand]
    private async Task LoadGuildsAsync()
    {
        if (_services?.Guilds is null) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var guilds = await _services.Guilds.GetCurrentUserGuildsAsync();
            var guildVms = new ObservableCollection<GuildViewModel>();

            foreach (var guild in guilds)
            {
                var guildVm = new GuildViewModel
                {
                    Id = guild.Id,
                    Name = guild.Name,
                    IconUrl = guild.GetIconUrl()
                };

                try
                {
                    var channels = await _services.Guilds.GetGuildChannelsAsync(guild.Id);

                    // Also fetch active threads
                    List<Channel> threads = [];
                    try
                    {
                        threads = await _services.Channels!.GetActiveThreadsAsync(guild.Id);
                    }
                    catch
                    {
                        // Threads may not be accessible
                    }

                    var tree = BuildChannelTree(channels, threads, guild.Id, guild.Name);
                    guildVm.Channels = new ObservableCollection<ChannelNodeViewModel>(tree);
                }
                catch (Exception ex)
                {
                    Log.Logger.Warning(ex, "Failed to load channels for guild {GuildId}", guild.Id);
                }

                guildVms.Add(guildVm);
            }

            Guilds = guildVms;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load guilds: {ex.Message}";
            Log.Logger.Error(ex, "Failed to load guilds");
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

    private static List<ChannelNodeViewModel> BuildChannelTree(
        List<Channel> channels, List<Channel> threads, string guildId, string guildName)
    {
        var result = new List<ChannelNodeViewModel>();
        var channelMap = new Dictionary<string, ChannelNodeViewModel>();

        // Create view models for all channels
        foreach (var channel in channels.OrderBy(c => c.Position ?? 0))
        {
            var vm = new ChannelNodeViewModel
            {
                Id = channel.Id,
                Name = channel.Name ?? "(unnamed)",
                GuildId = guildId,
                GuildName = guildName,
                Type = channel.Type
            };
            channelMap[channel.Id] = vm;
        }

        // Add threads as children of their parent channels
        foreach (var thread in threads)
        {
            var vm = new ChannelNodeViewModel
            {
                Id = thread.Id,
                Name = thread.Name ?? "(unnamed thread)",
                GuildId = guildId,
                GuildName = guildName,
                Type = thread.Type
            };

            if (thread.ParentId is not null && channelMap.TryGetValue(thread.ParentId, out var parent))
            {
                parent.Children.Add(vm);
            }
            else
            {
                channelMap[thread.Id] = vm;
            }
        }

        // Build tree: categories contain text channels
        foreach (var (id, vm) in channelMap)
        {
            var channel = channels.FirstOrDefault(c => c.Id == id);
            if (channel?.ParentId is not null && channelMap.TryGetValue(channel.ParentId, out var parentVm))
            {
                parentVm.Children.Add(vm);
            }
            else
            {
                result.Add(vm);
            }
        }

        return result;
    }
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
        4 => "",
        0 => "# ",
        5 => "# ",
        10 or 11 => "~ ",
        12 => "~ ",
        15 => "# ",
        _ => ""
    };

    public string FullDisplayName => $"{DisplayPrefix}{Name}";
}
