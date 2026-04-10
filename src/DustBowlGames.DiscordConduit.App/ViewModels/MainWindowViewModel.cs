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

    [RelayCommand]
    private void NavigateTo(string view)
    {
        CurrentView = view;
    }
}
