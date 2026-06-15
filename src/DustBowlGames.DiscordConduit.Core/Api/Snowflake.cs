namespace DustBowlGames.DiscordConduit.Core.Api;

/// <summary>
/// Helpers for validating Discord snowflake identifiers (channel, message, guild, user, webhook IDs).
/// A snowflake is an unsigned 64-bit integer rendered in decimal, so a valid value is a non-empty
/// string of ASCII digits no longer than 20 characters (the length of <see cref="ulong.MaxValue"/>).
/// </summary>
public static class Snowflake
{
    /// <summary>Maximum number of decimal digits in a 64-bit snowflake.</summary>
    private const int MaxDigits = 20; // ulong.MaxValue = 18446744073709551615

    /// <summary>
    /// Returns <c>true</c> if <paramref name="id"/> is a syntactically valid snowflake:
    /// non-null, 1–20 ASCII digits, and within the unsigned 64-bit range. This guards against
    /// untrusted IDs (e.g. user-typed slash-command options or a hand-crafted resume state file)
    /// being interpolated into Discord REST URL paths, where characters like <c>/</c>, <c>?</c>,
    /// <c>#</c> or <c>..</c> could otherwise redirect the bot's authenticated request to a
    /// different API route.
    /// </summary>
    /// <param name="id">The candidate identifier.</param>
    /// <returns><c>true</c> if the value is a valid snowflake; otherwise <c>false</c>.</returns>
    public static bool IsValid(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxDigits)
            return false;

        foreach (var c in id)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        // Reject values that exceed the 64-bit range (e.g. a 20-digit number above ulong.MaxValue).
        return ulong.TryParse(id, out _);
    }
}
