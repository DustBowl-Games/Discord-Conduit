using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustBowlGames.DiscordConduit.App.Services;
using DustBowlGames.DiscordConduit.Core.Api.Models;
using Serilog;

namespace DustBowlGames.DiscordConduit.App.ViewModels;

public partial class ProfileManagerViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<bool> _onConnectionChanged;

    [ObservableProperty]
    private ObservableCollection<ProfileItemViewModel> _profiles = [];

    [ObservableProperty]
    private ProfileItemViewModel? _selectedProfile;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private string _newProfileToken = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string? _connectedBotName;

    public ProfileManagerViewModel() : this(null!, _ => { }) { }

    public ProfileManagerViewModel(AppServices services, Action<bool> onConnectionChanged)
    {
        _services = services;
        _onConnectionChanged = onConnectionChanged;
    }

    [RelayCommand]
    private async Task LoadProfilesAsync()
    {
        if (_services is null) return;

        try
        {
            var profiles = await _services.ProfileManager.GetProfilesAsync();
            Profiles = new ObservableCollection<ProfileItemViewModel>(
                profiles.Select(p => new ProfileItemViewModel { Name = p.Name }));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load profiles: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName) || string.IsNullOrWhiteSpace(NewProfileToken))
        {
            ErrorMessage = "Profile name and token are required.";
            return;
        }

        ErrorMessage = null;

        try
        {
            await _services.ProfileManager.AddProfileAsync(NewProfileName.Trim(), NewProfileToken.Trim());
            Profiles.Add(new ProfileItemViewModel { Name = NewProfileName.Trim() });
            NewProfileName = string.Empty;
            NewProfileToken = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to add profile: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveProfileAsync()
    {
        if (SelectedProfile is null) return;

        try
        {
            await _services.ProfileManager.RemoveProfileAsync(SelectedProfile.Name);
            Profiles.Remove(SelectedProfile);

            if (IsConnected && ConnectedBotName?.Contains(SelectedProfile.Name) == true)
            {
                _services.Disconnect();
                IsConnected = false;
                ConnectedBotName = null;
                _onConnectionChanged(false);
            }

            SelectedProfile = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to remove profile: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedProfile is null) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var token = await _services.ProfileManager.GetTokenAsync(SelectedProfile.Name);
            if (string.IsNullOrEmpty(token))
            {
                ErrorMessage = "Token not found for this profile. Try re-adding it.";
                return;
            }

            _services.Connect(token);

            // Verify connection by calling GET /users/@me
            var botUser = await _services.RestClient!.GetAsync<User>("/users/@me");
            IsConnected = true;
            ConnectedBotName = $"Connected as {botUser.DisplayName}";
            _onConnectionChanged(true);

            Log.Logger.Information("Connected to Discord as {BotName} ({BotId})",
                botUser.DisplayName, botUser.Id);
        }
        catch (Exception ex)
        {
            _services.Disconnect();
            IsConnected = false;
            ConnectedBotName = null;
            _onConnectionChanged(false);
            ErrorMessage = $"Connection failed: {ex.Message}";
            Log.Logger.Error(ex, "Failed to connect to Discord");
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public partial class ProfileItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;
}
