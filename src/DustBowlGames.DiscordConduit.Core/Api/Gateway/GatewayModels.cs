using System.Text.Json;
using System.Text.Json.Serialization;
using DustBowlGames.DiscordConduit.Core.Api.Models;

namespace DustBowlGames.DiscordConduit.Core.Api.Gateway;

/// <summary>
/// Top-level gateway payload envelope sent and received over the WebSocket connection.
/// </summary>
public sealed class GatewayPayload
{
    /// <summary>Gateway opcode indicating the payload type.</summary>
    [JsonPropertyName("op")]
    public int Op { get; set; }

    /// <summary>Event data whose shape varies by opcode and event name.</summary>
    [JsonPropertyName("d")]
    public JsonElement? D { get; set; }

    /// <summary>Sequence number for resuming and heartbeats; null for non-dispatch payloads.</summary>
    [JsonPropertyName("s")]
    public int? S { get; set; }

    /// <summary>Event name for dispatch (op 0) payloads; null otherwise.</summary>
    [JsonPropertyName("t")]
    public string? T { get; set; }
}

/// <summary>
/// Payload data for the Hello (op 10) event, providing the heartbeat interval.
/// </summary>
public sealed class HelloData
{
    /// <summary>Interval in milliseconds between heartbeats.</summary>
    [JsonPropertyName("heartbeat_interval")]
    public int HeartbeatInterval { get; set; }
}

/// <summary>
/// Payload data for the READY dispatch event after successful identification.
/// </summary>
public sealed class ReadyData
{
    /// <summary>Session identifier used for resuming.</summary>
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    /// <summary>The bot user object.</summary>
    [JsonPropertyName("user")]
    public required User User { get; init; }

    /// <summary>Gateway URL to use when resuming the session.</summary>
    [JsonPropertyName("resume_gateway_url")]
    public required string ResumeGatewayUrl { get; init; }

    /// <summary>Partial application object containing the application ID.</summary>
    [JsonPropertyName("application")]
    public required ApplicationData Application { get; init; }
}

/// <summary>
/// Partial application object returned in the READY event.
/// </summary>
public sealed class ApplicationData
{
    /// <summary>The application's snowflake ID.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }
}

/// <summary>
/// Dispatch event fired when a user triggers an application command or component interaction.
/// </summary>
public sealed class InteractionCreateEvent
{
    /// <summary>The interaction's snowflake ID.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Interaction type (1 = Ping, 2 = ApplicationCommand, 3 = MessageComponent, etc.).</summary>
    [JsonPropertyName("type")]
    public int Type { get; init; }

    /// <summary>Command or component data, if applicable.</summary>
    [JsonPropertyName("data")]
    public InteractionData? Data { get; init; }

    /// <summary>Channel the interaction was sent from.</summary>
    [JsonPropertyName("channel_id")]
    public string? ChannelId { get; init; }

    /// <summary>Guild the interaction was sent from, if in a guild.</summary>
    [JsonPropertyName("guild_id")]
    public string? GuildId { get; init; }

    /// <summary>Continuation token for responding to the interaction.</summary>
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    /// <summary>Guild member data for the invoking user, if in a guild.</summary>
    [JsonPropertyName("member")]
    public GuildMember? Member { get; init; }
}

/// <summary>
/// Represents a guild member with optional user and nickname fields.
/// </summary>
public sealed class GuildMember
{
    /// <summary>The user object for this member, if present.</summary>
    [JsonPropertyName("user")]
    public User? User { get; init; }

    /// <summary>The member's guild-specific nickname, if set.</summary>
    [JsonPropertyName("nick")]
    public string? Nick { get; init; }
}

/// <summary>
/// Data payload describing the invoked command or component.
/// </summary>
public sealed class InteractionData
{
    /// <summary>Name of the invoked command. Null for component interactions.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Command type (1 = ChatInput, 2 = User, 3 = Message).</summary>
    [JsonPropertyName("type")]
    public int Type { get; init; }

    /// <summary>Parameters and values from the user, if any.</summary>
    [JsonPropertyName("options")]
    public List<InteractionOption>? Options { get; init; }

    /// <summary>ID of the target user or message for context-menu commands.</summary>
    [JsonPropertyName("target_id")]
    public string? TargetId { get; init; }

    /// <summary>Resolved data for IDs referenced in the interaction.</summary>
    [JsonPropertyName("resolved")]
    public ResolvedData? Resolved { get; init; }

    /// <summary>The custom ID of a component interaction (e.g. channel select).</summary>
    [JsonPropertyName("custom_id")]
    public string? CustomId { get; init; }

    /// <summary>Selected values from a select menu component interaction.</summary>
    [JsonPropertyName("values")]
    public List<string>? Values { get; init; }
}

/// <summary>
/// A single option supplied by the user for a slash command invocation.
/// </summary>
public sealed class InteractionOption
{
    /// <summary>Name of the parameter.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Option type (3 = String, 4 = Integer, 5 = Boolean, etc.).</summary>
    [JsonPropertyName("type")]
    public int Type { get; init; }

    /// <summary>Value of the option; the concrete type depends on <see cref="Type"/>.</summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; init; }
}

/// <summary>
/// Resolved objects (users, messages, etc.) referenced by ID in interaction data.
/// </summary>
public sealed class ResolvedData
{
    /// <summary>Map of message snowflake IDs to full message objects.</summary>
    [JsonPropertyName("messages")]
    public Dictionary<string, Message>? Messages { get; init; }
}

/// <summary>
/// Response payload sent back to Discord to answer an interaction.
/// </summary>
public sealed class InteractionResponse
{
    /// <summary>Response type (1 = Pong, 4 = ChannelMessageWithSource, 5 = DeferredChannelMessageWithSource, etc.).</summary>
    [JsonPropertyName("type")]
    public int Type { get; set; }

    /// <summary>Optional data for the response message.</summary>
    [JsonPropertyName("data")]
    public InteractionCallbackData? Data { get; set; }
}

/// <summary>
/// Callback data for an interaction response message.
/// </summary>
public sealed class InteractionCallbackData
{
    /// <summary>Text content of the response message.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>Message flags (e.g., 64 for ephemeral).</summary>
    [JsonPropertyName("flags")]
    public int? Flags { get; set; }

    /// <summary>Message components such as action rows.</summary>
    [JsonPropertyName("components")]
    public List<ActionRowComponent>? Components { get; set; }
}

/// <summary>
/// An action row component that holds other interactive components.
/// </summary>
public sealed class ActionRowComponent
{
    /// <summary>Component type; always 1 for action rows.</summary>
    [JsonPropertyName("type")]
    public int Type { get; set; } = 1;

    /// <summary>Child components within this action row.</summary>
    [JsonPropertyName("components")]
    public List<SelectMenuComponent>? Components { get; set; }
}

/// <summary>
/// A select menu component (e.g., channel select, string select).
/// </summary>
public sealed class SelectMenuComponent
{
    /// <summary>Component type (8 = channel select).</summary>
    [JsonPropertyName("type")]
    public int Type { get; set; } = 8;

    /// <summary>Developer-defined identifier for the component.</summary>
    [JsonPropertyName("custom_id")]
    public required string CustomId { get; init; }

    /// <summary>Placeholder text shown when nothing is selected.</summary>
    [JsonPropertyName("placeholder")]
    public string? Placeholder { get; set; }

    /// <summary>List of allowed channel types for channel select menus.</summary>
    [JsonPropertyName("channel_types")]
    public List<int>? ChannelTypes { get; set; }
}

/// <summary>
/// Represents a registered application command.
/// </summary>
public sealed class ApplicationCommand
{
    /// <summary>Name of the command (1-32 characters).</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Command type (1 = ChatInput, 2 = User, 3 = Message).</summary>
    [JsonPropertyName("type")]
    public int Type { get; init; }

    /// <summary>Description of the command (1-100 characters).</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Parameters for the command, if any.</summary>
    [JsonPropertyName("options")]
    public List<ApplicationCommandOption>? Options { get; init; }
}

/// <summary>
/// Describes a parameter for an application command.
/// </summary>
public sealed class ApplicationCommandOption
{
    /// <summary>Name of the parameter (1-32 characters).</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Option type (3 = String, 4 = Integer, 7 = Channel, etc.).</summary>
    [JsonPropertyName("type")]
    public int Type { get; init; }

    /// <summary>Description of the parameter (1-100 characters).</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Whether the parameter is required.</summary>
    [JsonPropertyName("required")]
    public bool? Required { get; init; }
}

/// <summary>
/// Response from GET /gateway/bot containing the WebSocket URL.
/// </summary>
public sealed class GatewayBotResponse
{
    /// <summary>The WSS URL to connect to.</summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }
}
