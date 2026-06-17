namespace DustBowlGames.DiscordConduit.Core.Export;

/// <summary>
/// The output format for a channel export.
/// </summary>
public enum ExportFormat
{
    /// <summary>Structured JSON (machine-readable; preserves all captured fields).</summary>
    Json,

    /// <summary>Comma-separated values, one row per message.</summary>
    Csv,

    /// <summary>Plain text, one readable line/block per message.</summary>
    Text,

    /// <summary>A self-contained, styled HTML document.</summary>
    Html,
}
