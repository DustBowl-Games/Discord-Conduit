using DustBowlGames.DiscordConduit.Core.Api.Models;
using DustBowlGames.DiscordConduit.Core.Migration;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Tests.Migration;

public class MessageMigratorExtrasTests
{
    private readonly MessageMigrator _migrator = new(new LoggerConfiguration().CreateLogger());

    private static Message Msg(string? content, StickerItem[]? stickers = null, string timestamp = "2024-06-15T12:00:00Z") => new()
    {
        Id = "1",
        ChannelId = "100",
        Author = new User { Id = "5", Username = "alice" },
        Content = content,
        Timestamp = timestamp,
        Type = 0,
        StickerItems = stickers?.ToList(),
    };

    private static StickerItem Sticker(int format) => new() { Id = "777", Name = "wave", FormatType = format };

    [Fact]
    public void BuildWebhookContent_IncludeStickers_AppendsPngStickerImageUrl()
    {
        var content = _migrator.BuildWebhookContent(Msg("hi", new[] { Sticker(1) }), null, includeStickers: true);

        Assert.Contains("hi", content);
        Assert.Contains("https://media.discordapp.net/stickers/777.png", content);
    }

    [Fact]
    public void BuildWebhookContent_GifSticker_AppendsGifUrl()
    {
        var content = _migrator.BuildWebhookContent(Msg(null, new[] { Sticker(4) }), null, includeStickers: true);

        Assert.Equal("https://media.discordapp.net/stickers/777.gif", content);
    }

    [Fact]
    public void BuildWebhookContent_LottieSticker_NotAppended()
    {
        var content = _migrator.BuildWebhookContent(Msg("hi", new[] { Sticker(3) }), null, includeStickers: true);

        Assert.Equal("hi", content); // Lottie (format 3) has no static image
    }

    [Fact]
    public void BuildWebhookContent_StickerOnlyMessage_ProducesStickerUrlContent()
    {
        var content = _migrator.BuildWebhookContent(Msg(null, new[] { Sticker(1) }), null, includeStickers: true);

        Assert.Equal("https://media.discordapp.net/stickers/777.png", content);
    }

    [Fact]
    public void BuildWebhookContent_IncludeTimestamp_AppendsTimestampFooter()
    {
        var content = _migrator.BuildWebhookContent(Msg("hi"), null, includeTimestamp: true);

        Assert.Contains("hi", content);
        Assert.Matches(@"<t:\d+:f>", content);
    }

    [Fact]
    public void BuildWebhookContent_StickersAndTimestampsOffByDefault()
    {
        var content = _migrator.BuildWebhookContent(Msg("hi", new[] { Sticker(1) }), null);

        Assert.Equal("hi", content); // defaults: no sticker URL, no timestamp footer
    }
}
