using DustBowlGames.DiscordConduit.Core.Api.Gateway;

namespace DustBowlGames.DiscordConduit.Core.Tests.Api;

/// <summary>
/// Tests for <see cref="DiscordGatewayClient.IsValidGatewayUrl"/>, which guards against connecting
/// to a tampered gateway URL (the bot token is sent immediately after connecting).
/// </summary>
public class DiscordGatewayClientTests
{
    [Theory]
    // Accepted: wss scheme, host within Discord's domains (apex or subdomain).
    [InlineData("wss://gateway.discord.gg")]
    [InlineData("wss://gateway-us-east1-d.discord.gg/?v=10&encoding=json")]
    [InlineData("wss://gateway.discord.gg/")]
    public void IsValidGatewayUrl_ValidDiscordUrls_ReturnsTrue(string url)
    {
        Assert.True(DiscordGatewayClient.IsValidGatewayUrl(url));
    }

    [Theory]
    [InlineData("ws://gateway.discord.gg")]            // not wss
    [InlineData("https://gateway.discord.gg")]         // wrong scheme
    [InlineData("wss://gateway.discord.gg.evil.com")]  // look-alike suffix
    [InlineData("wss://evil.com")]                       // unrelated host
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData(null)]
    public void IsValidGatewayUrl_InvalidUrls_ReturnsFalse(string? url)
    {
        Assert.False(DiscordGatewayClient.IsValidGatewayUrl(url));
    }
}
