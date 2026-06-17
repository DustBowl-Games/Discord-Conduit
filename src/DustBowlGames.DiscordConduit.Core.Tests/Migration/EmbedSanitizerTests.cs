using DustBowlGames.DiscordConduit.Core.Api.Models;
using DustBowlGames.DiscordConduit.Core.Migration;

namespace DustBowlGames.DiscordConduit.Core.Tests.Migration;

/// <summary>
/// Tests for <see cref="EmbedSanitizer"/>, which caps the embed count and strips any URL field
/// whose scheme is not http/https before the embeds are forwarded to a webhook.
/// </summary>
public class EmbedSanitizerTests
{
    private static Embed MakeEmbed(
        string? url = null,
        string? authorUrl = null,
        string? authorIconUrl = null,
        string? footerIconUrl = null,
        string? imageUrl = null,
        string? thumbnailUrl = null)
    {
        return new Embed
        {
            Title = "title",
            Type = "rich",
            Description = "desc",
            Url = url,
            Author = new EmbedAuthor { Name = "author", Url = authorUrl, IconUrl = authorIconUrl },
            Footer = new EmbedFooter { Text = "footer", IconUrl = footerIconUrl },
            Image = imageUrl is null ? null : new EmbedMedia { Url = imageUrl },
            Thumbnail = thumbnailUrl is null ? null : new EmbedMedia { Url = thumbnailUrl }
        };
    }

    [Fact]
    public void Sanitize_NullInput_ReturnsNull()
    {
        Assert.Null(EmbedSanitizer.Sanitize(null));
    }

    [Fact]
    public void Sanitize_EmptyList_ReturnsNull()
    {
        Assert.Null(EmbedSanitizer.Sanitize(new List<Embed>()));
    }

    [Fact]
    public void Sanitize_ThirteenEmbeds_CapsAtTen()
    {
        var embeds = Enumerable.Range(0, 13).Select(_ => MakeEmbed()).ToList();

        var result = EmbedSanitizer.Sanitize(embeds);

        Assert.NotNull(result);
        Assert.Equal(10, result.Count);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>1</script>")]
    [InlineData("ftp://example.com/file")]
    public void Sanitize_NonHttpUrlFields_AreNulledOut(string badUrl)
    {
        var embeds = new List<Embed>
        {
            MakeEmbed(url: badUrl, authorIconUrl: badUrl, authorUrl: badUrl, footerIconUrl: badUrl)
        };

        var result = EmbedSanitizer.Sanitize(embeds);

        Assert.NotNull(result);
        var embed = Assert.Single(result);
        Assert.Null(embed.Url);
        Assert.NotNull(embed.Author);
        Assert.Null(embed.Author.Url);
        Assert.Null(embed.Author.IconUrl);
        Assert.NotNull(embed.Footer);
        Assert.Null(embed.Footer.IconUrl);
    }

    [Theory]
    [InlineData("http://example.com/page")]
    [InlineData("https://example.com/page")]
    public void Sanitize_HttpAndHttpsUrls_ArePreserved(string goodUrl)
    {
        var embeds = new List<Embed>
        {
            MakeEmbed(url: goodUrl, authorIconUrl: goodUrl, authorUrl: goodUrl, footerIconUrl: goodUrl)
        };

        var result = EmbedSanitizer.Sanitize(embeds);

        Assert.NotNull(result);
        var embed = Assert.Single(result);
        Assert.Equal(goodUrl, embed.Url);
        Assert.NotNull(embed.Author);
        Assert.Equal(goodUrl, embed.Author.Url);
        Assert.Equal(goodUrl, embed.Author.IconUrl);
        Assert.NotNull(embed.Footer);
        Assert.Equal(goodUrl, embed.Footer.IconUrl);
    }

    [Fact]
    public void Sanitize_NonHttpImageAndThumbnail_AreDropped()
    {
        var embeds = new List<Embed>
        {
            MakeEmbed(imageUrl: "javascript:alert(1)", thumbnailUrl: "data:text/html,x")
        };

        var result = EmbedSanitizer.Sanitize(embeds);

        Assert.NotNull(result);
        var embed = Assert.Single(result);
        Assert.Null(embed.Image);
        Assert.Null(embed.Thumbnail);
    }

    [Fact]
    public void Sanitize_HttpImageAndThumbnail_ArePreserved()
    {
        var embeds = new List<Embed>
        {
            MakeEmbed(imageUrl: "https://cdn.example.com/i.png", thumbnailUrl: "http://cdn.example.com/t.png")
        };

        var result = EmbedSanitizer.Sanitize(embeds);

        Assert.NotNull(result);
        var embed = Assert.Single(result);
        Assert.NotNull(embed.Image);
        Assert.Equal("https://cdn.example.com/i.png", embed.Image.Url);
        Assert.NotNull(embed.Thumbnail);
        Assert.Equal("http://cdn.example.com/t.png", embed.Thumbnail.Url);
    }
}
