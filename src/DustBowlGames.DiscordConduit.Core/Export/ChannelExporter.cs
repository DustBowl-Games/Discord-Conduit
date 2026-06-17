using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Api.Models;
using DustBowlGames.DiscordConduit.Core.Migration;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Export;

/// <summary>
/// Exports a channel's messages to a file in JSON, CSV, plain text, or HTML. Unlike a migration,
/// an export only reads — nothing is posted to Discord. Attachment CDN URLs are referenced as-is
/// (note that Discord CDN links expire); the file is written atomically.
/// </summary>
public sealed class ChannelExporter
{
    private readonly MessageEndpoints _messageEndpoints;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new channel exporter.
    /// </summary>
    /// <param name="messageEndpoints">Endpoint class for fetching messages.</param>
    /// <param name="logger">Logger instance.</param>
    public ChannelExporter(MessageEndpoints messageEndpoints, ILogger logger)
    {
        _messageEndpoints = messageEndpoints;
        _logger = logger;
    }

    /// <summary>
    /// Fetches all messages from the channel (oldest first), applies the optional filter, formats
    /// them, and writes the result to <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="channelId">The channel snowflake ID to export.</param>
    /// <param name="format">The output format.</param>
    /// <param name="outputPath">The file path to write.</param>
    /// <param name="filter">Optional message filter; defaults to all messages.</param>
    /// <param name="progress">Optional progress reporter (number of messages fetched so far).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of messages written.</returns>
    public async Task<int> ExportAsync(
        string channelId,
        ExportFormat format,
        string outputPath,
        MessageFilter? filter = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        _logger.Information("Exporting channel {ChannelId} to {Path} as {Format}", channelId, outputPath, format);

        var messages = await FetchAllChronologicalAsync(channelId, progress, ct).ConfigureAwait(false);

        if (filter is { IsActive: true })
        {
            var before = messages.Count;
            messages = messages.Where(filter.Matches).ToList();
            _logger.Information("Filter applied: {Kept} of {Total} messages exported", messages.Count, before);
        }

        var rendered = format switch
        {
            ExportFormat.Json => RenderJson(channelId, messages),
            ExportFormat.Csv => RenderCsv(messages),
            ExportFormat.Text => RenderText(messages),
            ExportFormat.Html => RenderHtml(channelId, messages),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format."),
        };

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Atomic write: temp file then move, so an interrupted export never leaves a partial file.
        var tempPath = outputPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, rendered, new UTF8Encoding(false), ct).ConfigureAwait(false);
        File.Move(tempPath, outputPath, overwrite: true);

        _logger.Information("Exported {Count} messages to {Path}", messages.Count, outputPath);
        return messages.Count;
    }

    private async Task<List<Message>> FetchAllChronologicalAsync(string channelId, IProgress<int>? progress, CancellationToken ct)
    {
        var all = new List<Message>();
        string? beforeId = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var batch = await _messageEndpoints.GetMessagesAsync(channelId, 100, before: beforeId, ct: ct).ConfigureAwait(false);
            if (batch.Count == 0)
                break;

            all.AddRange(batch);
            beforeId = batch[^1].Id;
            progress?.Report(all.Count);

            if (batch.Count < 100)
                break;
        }

        all.Reverse(); // GetMessages returns newest-first; export oldest-first.
        return all;
    }

    // ── Formatters (internal static for direct unit testing) ──────────────────

    internal static string RenderJson(string channelId, List<Message> messages)
    {
        var doc = new
        {
            channel_id = channelId,
            exported_at = DateTimeOffset.UtcNow,
            message_count = messages.Count,
            messages = messages.Select(m => new
            {
                id = m.Id,
                type = m.Type,
                timestamp = m.Timestamp,
                edited_timestamp = m.EditedTimestamp,
                pinned = m.Pinned,
                author = new { id = m.Author.Id, name = m.Author.DisplayName },
                content = m.Content,
                attachments = (m.Attachments ?? []).Select(a => new { a.Filename, a.Url, a.Size }),
                reactions = (m.Reactions ?? []).Select(r => new { emoji = r.Emoji.ApiIdentifier, r.Count }),
                has_poll = m.Poll is not null,
            }),
        };

        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static string RenderCsv(List<Message> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("timestamp,author_id,author,pinned,type,content,attachments");

        foreach (var m in messages)
        {
            var attachments = string.Join(" | ", (m.Attachments ?? []).Select(a => a.Url));
            sb.Append(Csv(m.Timestamp)).Append(',')
              .Append(Csv(m.Author.Id)).Append(',')
              .Append(Csv(m.Author.DisplayName)).Append(',')
              .Append(m.Pinned ? "true" : "false").Append(',')
              .Append(m.Type.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv(m.Content ?? string.Empty)).Append(',')
              .Append(Csv(attachments))
              .Append('\n');
        }

        return sb.ToString();
    }

    internal static string RenderText(List<Message> messages)
    {
        var sb = new StringBuilder();

        foreach (var m in messages)
        {
            sb.Append('[').Append(m.Timestamp).Append("] ")
              .Append(m.Author.DisplayName);
            if (m.Pinned) sb.Append(" (pinned)");
            sb.Append(':').Append('\n');

            if (!string.IsNullOrEmpty(m.Content))
                sb.Append(m.Content).Append('\n');

            foreach (var a in m.Attachments ?? [])
                sb.Append("  [attachment] ").Append(a.Filename).Append(" - ").Append(a.Url).Append('\n');

            if (m.Poll is not null)
                sb.Append("  [poll] ").Append(m.Poll.Question?.Text ?? "(poll)").Append('\n');

            sb.Append('\n');
        }

        return sb.ToString();
    }

    internal static string RenderHtml(string channelId, List<Message> messages)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Discord Conduit Export</title>
            <style>
              body { background:#313338; color:#dbdee1; font-family:'gg sans',system-ui,Arial,sans-serif; margin:0; padding:24px; }
              .header { color:#f2f3f5; font-size:14px; opacity:.7; margin-bottom:16px; }
              .msg { padding:6px 0; border-top:1px solid #3f4147; }
              .meta { font-size:13px; }
              .author { color:#f2f3f5; font-weight:600; }
              .ts { color:#949ba4; font-size:11px; margin-left:8px; }
              .pin { color:#f0b232; font-size:11px; margin-left:8px; }
              .content { white-space:pre-wrap; word-wrap:break-word; margin-top:2px; }
              .att { display:block; margin-top:4px; }
              .att img { max-width:400px; max-height:300px; border-radius:6px; }
              .att a { color:#00a8fc; }
              .poll { color:#949ba4; font-style:italic; margin-top:4px; }
            </style></head><body>
            """);
        sb.Append("<div class=\"header\">Export of channel ").Append(Html(channelId))
          .Append(" &middot; ").Append(messages.Count).Append(" messages &middot; generated ")
          .Append(Html(DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture))).Append("</div>\n");

        foreach (var m in messages)
        {
            sb.Append("<div class=\"msg\"><div class=\"meta\"><span class=\"author\">")
              .Append(Html(m.Author.DisplayName)).Append("</span><span class=\"ts\">")
              .Append(Html(m.Timestamp)).Append("</span>");
            if (m.Pinned) sb.Append("<span class=\"pin\">📌 pinned</span>");
            sb.Append("</div>");

            if (!string.IsNullOrEmpty(m.Content))
                sb.Append("<div class=\"content\">").Append(Html(m.Content)).Append("</div>");

            foreach (var a in m.Attachments ?? [])
            {
                sb.Append("<span class=\"att\">");
                if (IsImage(a))
                    sb.Append("<a href=\"").Append(Html(a.Url)).Append("\"><img src=\"").Append(Html(a.Url))
                      .Append("\" alt=\"").Append(Html(a.Filename)).Append("\"></a>");
                else
                    sb.Append("<a href=\"").Append(Html(a.Url)).Append("\">📎 ").Append(Html(a.Filename)).Append("</a>");
                sb.Append("</span>");
            }

            if (m.Poll is not null)
                sb.Append("<div class=\"poll\">📊 Poll: ").Append(Html(m.Poll.Question?.Text ?? "(poll)")).Append("</div>");

            sb.Append("</div>\n");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static bool IsImage(Attachment a) =>
        a.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
        || a.Width is > 0;

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Csv(string value)
    {
        // Always quote and double internal quotes — safe for commas, quotes, and newlines.
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
