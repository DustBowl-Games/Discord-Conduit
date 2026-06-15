using DustBowlGames.DiscordConduit.Core.Api;

namespace DustBowlGames.DiscordConduit.Core.Tests.Api;

public class SnowflakeTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("123456789012345678")]
    [InlineData("18446744073709551615")] // ulong.MaxValue (20 digits)
    public void IsValid_AcceptsValidSnowflakes(string id)
    {
        Assert.True(Snowflake.IsValid(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12a")]
    [InlineData("1/2")]
    [InlineData("x/../../../../guilds/999/bans")] // URL path-injection attempt
    [InlineData("0&limit=1")]                      // query-injection attempt
    [InlineData(" 123")]                           // leading whitespace
    [InlineData("123456789012345678901")]          // 21 digits (too long)
    [InlineData("99999999999999999999")]           // 20 digits but > ulong.MaxValue
    public void IsValid_RejectsInvalidOrUnsafe(string? id)
    {
        Assert.False(Snowflake.IsValid(id));
    }
}
