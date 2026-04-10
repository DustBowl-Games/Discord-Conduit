using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustBowlGames.DiscordConduit.Core.Migration;

namespace DustBowlGames.DiscordConduit.App.ViewModels;

public partial class MigrationPreviewViewModel : ObservableObject
{
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

    public void LoadPreview(MigrationPreview preview)
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

    [RelayCommand]
    private async Task RunPreviewAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // TODO: Wire up MigrationEngine.PreviewAsync
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Preview failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
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
