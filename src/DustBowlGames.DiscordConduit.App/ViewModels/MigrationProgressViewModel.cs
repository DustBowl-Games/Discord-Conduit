using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustBowlGames.DiscordConduit.Core.Migration;

namespace DustBowlGames.DiscordConduit.App.ViewModels;

public partial class MigrationProgressViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private int _completed;

    [ObservableProperty]
    private int _total;

    [ObservableProperty]
    private int _failed;

    [ObservableProperty]
    private int _skipped;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string? _currentMessagePreview;

    [ObservableProperty]
    private string _elapsedTime = "00:00:00";

    [ObservableProperty]
    private string? _estimatedRemaining;

    [ObservableProperty]
    private string _phase = "Ready";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private MigrationResult? _result;

    public void UpdateProgress(MigrationProgress progress)
    {
        Completed = progress.Completed;
        Total = progress.Total;
        Failed = progress.Failed;
        Skipped = progress.Skipped;
        CurrentMessagePreview = progress.CurrentMessagePreview;
        ElapsedTime = progress.Elapsed.ToString(@"hh\:mm\:ss");
        EstimatedRemaining = progress.EstimatedRemaining?.ToString(@"hh\:mm\:ss");
        ProgressPercent = progress.Total > 0 ? (double)progress.Completed / progress.Total * 100 : 0;
        Phase = progress.Phase.ToString();
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        IsRunning = false;
    }

    [RelayCommand]
    private void TogglePause()
    {
        IsPaused = !IsPaused;
        // TODO: Wire up PauseTokenSource
    }

    [RelayCommand]
    private async Task StartMigrationAsync()
    {
        _cts = new CancellationTokenSource();
        IsRunning = true;
        IsComplete = false;
        ErrorMessage = null;

        var progress = new Progress<MigrationProgress>(UpdateProgress);

        try
        {
            // TODO: Wire up MigrationEngine.RunAsync
            IsComplete = true;
        }
        catch (OperationCanceledException)
        {
            Phase = "Cancelled";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Migration failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }
}
