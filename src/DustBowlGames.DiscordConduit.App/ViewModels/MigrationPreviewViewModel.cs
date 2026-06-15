using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustBowlGames.DiscordConduit.App.Services;
using DustBowlGames.DiscordConduit.Core.Migration;
using Serilog;

namespace DustBowlGames.DiscordConduit.App.ViewModels;

public partial class MigrationPreviewViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Func<ChannelNodeViewModel?> _getSource;
    private readonly Func<ChannelNodeViewModel?> _getDestination;

    [ObservableProperty]
    private int _messageCount;

    [ObservableProperty]
    private int _attachmentCount;

    [ObservableProperty]
    private string _totalAttachmentSize = "0 MB";

    [ObservableProperty]
    private string _estimatedDuration = "--";

    [ObservableProperty]
    private ObservableCollection<string> _warnings = [];

    [ObservableProperty]
    private ObservableCollection<OversizedAttachmentViewModel> _oversizedAttachments = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasPreview;

    [ObservableProperty]
    private bool _isDryRun;

    [ObservableProperty]
    private bool _includeReactions = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _sourceChannelName;

    [ObservableProperty]
    private string? _destinationChannelName;

    public MigrationPreviewViewModel() : this(null!, () => null, () => null) { }

    public MigrationPreviewViewModel(
        AppServices services,
        Func<ChannelNodeViewModel?> getSource,
        Func<ChannelNodeViewModel?> getDestination)
    {
        _services = services;
        _getSource = getSource;
        _getDestination = getDestination;
    }

    public MigrationOptions? CurrentOptions { get; private set; }

    [RelayCommand]
    private async Task RunPreviewAsync()
    {
        var source = _getSource();
        var dest = _getDestination();

        if (source is null || dest is null)
        {
            ErrorMessage = "Select a source and destination channel first.";
            return;
        }

        if (_services?.Migration is null)
        {
            ErrorMessage = "Not connected to Discord.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        SourceChannelName = source.FullDisplayName;
        DestinationChannelName = dest.FullDisplayName;

        try
        {
            CurrentOptions = new MigrationOptions(
                source.Id, dest.Id, source.GuildId, IsDryRun, IncludeReactions);

            var preview = await _services.Migration.PreviewAsync(CurrentOptions, CancellationToken.None);
            LoadPreview(preview);

            await ValidatePermissionsAsync(source, dest, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Preview failed: {ex.Message}";
            Log.Logger.Error(ex, "Migration preview failed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Checks the bot's permissions on the source/destination channels and surfaces any
    /// problems as warnings. Permission failures should not abort the preview, so any
    /// exception is logged and treated as "could not verify permissions".
    /// </summary>
    private async Task ValidatePermissionsAsync(
        ChannelNodeViewModel source, ChannelNodeViewModel dest, CancellationToken ct)
    {
        if (_services?.Validator is null) return;

        try
        {
            var result = await _services.Validator.ValidateAsync(source.Id, dest.Id, source.GuildId, ct);
            if (!result.IsValid)
            {
                foreach (var issue in result.Issues)
                {
                    Warnings.Add(issue.Description);
                }
            }
        }
        catch (Exception ex)
        {
            Warnings.Add("Could not verify bot permissions; the migration may fail if permissions are missing.");
            Log.Logger.Warning(ex, "Permission validation failed during preview");
        }
    }

    private void LoadPreview(MigrationPreview preview)
    {
        MessageCount = preview.MessageCount;
        AttachmentCount = preview.AttachmentCount;
        TotalAttachmentSize = FormatBytes(preview.TotalAttachmentBytes);
        EstimatedDuration = preview.EstimatedDuration.ToString(@"hh\:mm\:ss");
        Warnings = new ObservableCollection<string>(preview.Warnings);
        OversizedAttachments = new ObservableCollection<OversizedAttachmentViewModel>(
            preview.OversizedAttachments.Select(a => new OversizedAttachmentViewModel
            {
                Filename = a.Filename,
                Size = FormatBytes(a.SizeBytes),
                SourceMessageId = a.SourceMessageId
            }));
        HasPreview = true;
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
    }
}

public partial class OversizedAttachmentViewModel : ObservableObject
{
    [ObservableProperty]
    private string _filename = string.Empty;

    [ObservableProperty]
    private string _size = string.Empty;

    [ObservableProperty]
    private string _sourceMessageId = string.Empty;
}
