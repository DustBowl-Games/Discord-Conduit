using DustBowlGames.DiscordConduit.Core.Api.Models;
using DustBowlGames.DiscordConduit.Core.Migration;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Tests.Migration;

public class MessageMigratorTests
{
    private readonly MessageMigrator _migrator = new(new LoggerConfiguration().CreateLogger());
    private readonly Dictionary<string, string> _emptyMap = new();

    private static User CreateUser(string id = "100", string username = "testuser", string? globalName = null, string? avatar = null)
    {
        return new User
        {
            Id = id,
            Username = username,
            GlobalName = globalName,
            Avatar = avatar
        };
    }

    private static Message CreateMessage(
        string id = "1",
        int type = 0,
        string? content = "hello",
        User? author = null,
        Message? referencedMessage = null,
        MessageReference? messageReference = null)
    {
        return new Message
        {
            Id = id,
            ChannelId = "chan1",
            Author = author ?? CreateUser(),
            Content = content,
            Timestamp = "2024-01-01T00:00:00Z",
            Type = type,
            ReferencedMessage = referencedMessage,
            MessageReference = messageReference
        };
    }

    // --- BuildReplyReference ---

    [Fact]
    public void BuildReplyReference_DefaultMessage_ReturnsNull()
    {
        var message = CreateMessage(type: 0);

        var result = _migrator.BuildReplyReference(message, _emptyMap);

        Assert.Null(result);
    }

    [Fact]
    public void BuildReplyReference_ReplyTypeButNullReferencedMessage_ReturnsNull()
    {
        var message = CreateMessage(type: 19, referencedMessage: null,
            messageReference: new MessageReference { MessageId = "999" });

        var result = _migrator.BuildReplyReference(message, _emptyMap);

        Assert.Null(result);
    }

    [Fact]
    public void BuildReplyReference_ReplyWithReferencedMessage_FormatsCorrectly()
    {
        var refAuthor = CreateUser(globalName: "DisplayUser");
        var refMessage = CreateMessage(id: "50", author: refAuthor, content: "original content");
        var message = CreateMessage(type: 19, referencedMessage: refMessage,
            messageReference: new MessageReference { MessageId = "50" });

        var result = _migrator.BuildReplyReference(message, _emptyMap);

        Assert.NotNull(result);
        Assert.Equal("↩ replying to @DisplayUser: \"original content\"", result);
    }

    [Fact]
    public void BuildReplyReference_LongContent_TruncatesTo100Chars()
    {
        var longContent = new string('x', 150);
        var refMessage = CreateMessage(id: "50", content: longContent);
        var message = CreateMessage(type: 19, referencedMessage: refMessage,
            messageReference: new MessageReference { MessageId = "50" });

        var result = _migrator.BuildReplyReference(message, _emptyMap);

        Assert.NotNull(result);
        Assert.Contains(new string('x', 100) + "...", result);
    }

    [Fact]
    public void BuildReplyReference_NullContent_UsesEmptyString()
    {
        var refMessage = CreateMessage(id: "50", content: null);
        var message = CreateMessage(type: 19, referencedMessage: refMessage,
            messageReference: new MessageReference { MessageId = "50" });

        var result = _migrator.BuildReplyReference(message, _emptyMap);

        Assert.NotNull(result);
        Assert.Contains("\"\"", result);
    }

    [Fact]
    public void BuildReplyReference_AuthorNameWithControlAndBidiChars_StripsThem()
    {
        // Display name laced with a zero-width space, an RLO bidi override, a BOM and a control
        // char. Built from numeric escapes so the test is unaffected by source-file encoding.
        const char zwsp = '​'; // zero-width space
        const char rlo = '‮';  // right-to-left override
        const char bom = '﻿';  // BOM / ZWNBSP
        const char bell = ''; // C0 control
        var rawName = $"Ev{zwsp}il{rlo}user{bom}{bell}";
        var refAuthor = CreateUser(globalName: rawName);
        var refMessage = CreateMessage(id: "50", author: refAuthor, content: "hi");
        var message = CreateMessage(type: 19, referencedMessage: refMessage,
            messageReference: new MessageReference { MessageId = "50" });

        var result = _migrator.BuildReplyReference(message, _emptyMap);

        Assert.NotNull(result);
        Assert.Equal("↩ replying to @Eviluser: \"hi\"", result);
        // None of the stripped code points may survive in the output line.
        Assert.DoesNotContain(zwsp, result);
        Assert.DoesNotContain(rlo, result);
        Assert.DoesNotContain(bom, result);
        Assert.DoesNotContain(bell, result);
    }

    [Fact]
    public void BuildReplyReference_MultiLineContent_ProducesSingleLineQuote()
    {
        var refMessage = CreateMessage(id: "50", content: "line1\nline2");
        var message = CreateMessage(type: 19, referencedMessage: refMessage,
            messageReference: new MessageReference { MessageId = "50" });

        var result = _migrator.BuildReplyReference(message, _emptyMap);

        Assert.NotNull(result);
        // The newline must be collapsed so the reply reference stays on a single line.
        Assert.DoesNotContain('\n', result);
        Assert.Equal("↩ replying to @testuser: \"line1 line2\"", result);
    }

    // --- BuildWebhookContent ---

    [Fact]
    public void BuildWebhookContent_NullReply_ReturnsMessageContent()
    {
        var message = CreateMessage(content: "some text");

        var result = _migrator.BuildWebhookContent(message, null);

        Assert.Equal("some text", result);
    }

    [Fact]
    public void BuildWebhookContent_WithReplyAndContent_CombinesWithNewline()
    {
        var message = CreateMessage(content: "my reply");
        var reply = "↩ replying to @User: \"original\"";

        var result = _migrator.BuildWebhookContent(message, reply);

        Assert.Equal($"{reply}\nmy reply", result);
    }

    [Fact]
    public void BuildWebhookContent_WithReplyButNullContent_ReturnsReplyOnly()
    {
        var message = CreateMessage(content: null);
        var reply = "↩ replying to @User: \"original\"";

        var result = _migrator.BuildWebhookContent(message, reply);

        Assert.Equal(reply, result);
    }

    [Fact]
    public void BuildWebhookContent_WithReplyButEmptyContent_ReturnsReplyOnly()
    {
        var message = CreateMessage(content: "");
        var reply = "↩ replying to @User: \"original\"";

        var result = _migrator.BuildWebhookContent(message, reply);

        Assert.Equal(reply, result);
    }

    [Fact]
    public void BuildWebhookContent_NullContentNullReply_ReturnsNull()
    {
        var message = CreateMessage(content: null);

        var result = _migrator.BuildWebhookContent(message, null);

        Assert.Null(result);
    }

    // --- GetWebhookUsername ---

    [Fact]
    public void GetWebhookUsername_UserWithGlobalName_ReturnsGlobalName()
    {
        var author = CreateUser(username: "raw", globalName: "Pretty Name");
        var message = CreateMessage(author: author);

        var result = _migrator.GetWebhookUsername(message);

        Assert.Equal("Pretty Name", result);
    }

    [Fact]
    public void GetWebhookUsername_UserWithoutGlobalName_ReturnsUsername()
    {
        var author = CreateUser(username: "rawuser", globalName: null);
        var message = CreateMessage(author: author);

        var result = _migrator.GetWebhookUsername(message);

        Assert.Equal("rawuser", result);
    }

    // --- GetWebhookAvatarUrl ---

    [Fact]
    public void GetWebhookAvatarUrl_UserWithAvatar_ReturnsCdnUrl()
    {
        var author = CreateUser(id: "42", avatar: "abc123");
        var message = CreateMessage(author: author);

        var result = _migrator.GetWebhookAvatarUrl(message);

        Assert.Equal("https://cdn.discordapp.com/avatars/42/abc123.png", result);
    }

    [Fact]
    public void GetWebhookAvatarUrl_AnimatedAvatar_ReturnsPngUrl()
    {
        // Webhook avatar_url always uses .png (Discord rejects .gif for webhooks)
        var author = CreateUser(id: "42", avatar: "a_animated123");
        var message = CreateMessage(author: author);

        var result = _migrator.GetWebhookAvatarUrl(message);

        Assert.Equal("https://cdn.discordapp.com/avatars/42/a_animated123.png", result);
    }

    [Fact]
    public void GetWebhookAvatarUrl_NoAvatar_ReturnsDefaultAvatarUrl()
    {
        var author = CreateUser(avatar: null);
        var message = CreateMessage(author: author);

        var result = _migrator.GetWebhookAvatarUrl(message);

        Assert.StartsWith("https://cdn.discordapp.com/embed/avatars/", result);
    }
}
