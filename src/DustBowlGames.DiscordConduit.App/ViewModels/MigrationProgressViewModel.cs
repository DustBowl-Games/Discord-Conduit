using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustBowlGames.DiscordConduit.App.Services;
using DustBowlGames.DiscordConduit.Core.Migration;
using Serilog;

namespace DustBowlGames.DiscordConduit.App.ViewModels;

public partial class MigrationProgressViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Func<MigrationOptions?> _getOptions;
    private CancellationTokenSource? _cts;
    private PauseTokenSource? _pauseSource;

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

    // Summary properties for post-migration display
    [ObservableProperty]
    private string _summaryText = string.Empty;

    public MigrationProgressViewModel() : this(null!, () => null) { }

    public MigrationProgressViewModel(AppServices services, Func<MigrationOptions?> getOptions)
    {
        _services = services;
        _getOptions = getOptions;
    }

    private void UpdateProgress(MigrationProgress progress)
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
    }

    [RelayCommand]
    private void TogglePause()
    {
        IsPaused = !IsPaused;

        if (IsPaused)
        {
            _pauseSource?.Pause();
        }
        else
        {
            _pauseSource?.Resume();
        }
    }

    [RelayCommand]
    private async Task StartMigrationAsync()
    {
        // Re-entrancy guard: prevent a second start from overwriting _cts/_pauseSource
        // and launching a concurrent migration run.
        if (IsRunning) return;

        var options = _getOptions();
        if (options is null)
        {
            ErrorMessage = "No migration options set. Run a preview first.";
            return;
        }

        if (_services?.Migration is null)
        {
            ErrorMessage = "Not connected to Discord.";
            return;
        }

        _cts = new CancellationTokenSource();
        _pauseSource = new PauseTokenSource();
        IsRunning = true;
        IsPaused = false;
        IsComplete = false;
        ErrorMessage = null;
        Phase = "Starting...";

        var progress = new Progress<MigrationProgress>(UpdateProgress);

        try
        {
            Result = await _services.Migration.RunAsync(options, progress, _cts.Token, _pauseSource.Token);
            IsComplete = true;
            Phase = "Complete";
            var skippedNote = Result.TotalSkipped > 0 ? " (skipped = system messages)" : string.Empty;
            SummaryText = $"Migrated {Result.TotalMigrated} messages, {Result.TotalFailed} failed, {Result.TotalSkipped} skipped in {Result.Duration:hh\\:mm\\:ss}{skippedNote}";
            Log.Logger.Information("Migration completed: {Summary}", SummaryText);
        }
        catch (OperationCanceledException)
        {
            Phase = "Cancelled";
            SummaryText = "Migration was cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Migration failed: {ex.Message}";
            Log.Logger.Error(ex, "Migration failed");
        }
        finally
        {
            IsRunning = false;
            IsPaused = false;
            _pauseSource = null;

            var cts = _cts;
            _cts = null;
            cts?.Dispose();
        }
    }
}
