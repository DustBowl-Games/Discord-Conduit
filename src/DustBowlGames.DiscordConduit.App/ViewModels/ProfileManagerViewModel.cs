using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DustBowlGames.DiscordConduit.App.ViewModels;

public partial class ProfileManagerViewModel : ObservableObject
{
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

    [RelayCommand]
    private async Task AddProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName) || string.IsNullOrWhiteSpace(NewProfileToken))
        {
            ErrorMessage = "Profile name and token are required.";
            return;
        }

        ErrorMessage = null;
        // TODO: Wire up ProfileManager.AddProfileAsync
        Profiles.Add(new ProfileItemViewModel { Name = NewProfileName });
        NewProfileName = string.Empty;
        NewProfileToken = string.Empty;
    }

    [RelayCommand]
    private async Task RemoveProfileAsync()
    {
        if (SelectedProfile is null) return;

        // TODO: Wire up ProfileManager.RemoveProfileAsync
        Profiles.Remove(SelectedProfile);
        SelectedProfile = null;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedProfile is null) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // TODO: Wire up — get token, create DiscordRestClient, call GET /users/@me
            IsConnected = true;
            ConnectedBotName = $"Connected as bot (placeholder)";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
            IsConnected = false;
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
