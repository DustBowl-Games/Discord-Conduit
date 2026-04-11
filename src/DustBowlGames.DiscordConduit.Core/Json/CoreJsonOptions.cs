using System.Text.Json;
using System.Text.Json.Serialization;

namespace DustBowlGames.DiscordConduit.Core.Json;

/// <summary>Shared JSON serialization options for the Discord API.</summary>
public static class CoreJsonOptions
{
    /// <summary>Default options: snake_case naming, skip null values.</summary>
    public static JsonSerializerOptions Default { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
