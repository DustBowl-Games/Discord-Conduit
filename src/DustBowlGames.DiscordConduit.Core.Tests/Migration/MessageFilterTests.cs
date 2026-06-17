using DustBowlGames.DiscordConduit.Core.Api.Models;
using DustBowlGames.DiscordConduit.Core.Migration;

namespace DustBowlGames.DiscordConduit.Core.Tests.Migration;

/// <summary>
/// Unit tests for <see cref="MessageFilter"/> — the pure predicate that narrows which messages a
/// migration moves. All criteria combine with logical AND; an all-default filter matches everything.
/// </summary>
public class MessageFilterTests
{
    private static User CreateUser(string id = "100", string username = "testuser", bool? bot = null)
    {
        return new User
        {
            Id = id,
            Username = username,
            Bot = bot
        };
    }

    private static Message CreateMessage(
        string id = "1",
        string authorId = "100",
        bool? authorBot = null,
        string? content = "hello",
        string timestamp = "2024-06-15T12:00:00Z",
        List<Attachment>? attachments = null)
    {
        return new Message
        {
            Id = id,
            ChannelId = "chan1",
            Author = CreateUser(id: authorId, bot: authorBot),
            Content = content,
            Timestamp = timestamp,
            Attachments = attachments
        };
    }

    private static Attachment CreateAttachment(string id = "a1", string filename = "file.png", long size = 1024)
    {
        return new Attachment
        {
            Id = id,
            Filename = filename,
            Size = size,
            Url = $"https://cdn.example.com/{filename}"
        };
    }

    // --- IsActive ---

    [Fact]
    public void IsActive_AllDefaults_IsFalse()
    {
        var filter = new MessageFilter();

        Assert.False(filter.IsActive);
    }

    [Fact]
    public void IsActive_AllDefaults_MatchesEverything()
    {
        var filter = new MessageFilter();

        Assert.True(filter.Matches(CreateMessage()));
        Assert.True(filter.Matches(CreateMessage(authorId: "999", authorBot: true, content: null, attachments: null)));
    }

    [Theory]
    [InlineData("777", null, null, null, false, false)]
    [InlineData(null, "2024-01-01T00:00:00Z", null, null, false, false)]
    [InlineData(null, null, "2024-01-01T00:00:00Z", null, false, false)]
    [InlineData(null, null, null, "needle", false, false)]
    [InlineData(null, null, null, null, true, false)]
    [InlineData(null, null, null, null, false, true)]
    public void IsActive_AnySingleCriterion_IsTrue(
        string? authorId, string? since, string? until, string? contentContains, bool attachmentsOnly, bool excludeBots)
    {
        var filter = new MessageFilter(
            AuthorId: authorId,
            Since: since is null ? null : DateTimeOffset.Parse(since),
            Until: until is null ? null : DateTimeOffset.Parse(until),
            ContentContains: contentContains,
            AttachmentsOnly: attachmentsOnly,
            ExcludeBots: excludeBots);

        Assert.True(filter.IsActive);
    }

    // --- AuthorId ---

    [Fact]
    public void AuthorId_OnlyMatchesThatAuthor()
    {
        var filter = new MessageFilter(AuthorId: "100");

        Assert.True(filter.Matches(CreateMessage(authorId: "100")));
        Assert.False(filter.Matches(CreateMessage(authorId: "200")));
    }

    // --- ExcludeBots ---

    [Fact]
    public void ExcludeBots_BotAuthoredMessage_IsExcluded()
    {
        var filter = new MessageFilter(ExcludeBots: true);

        Assert.False(filter.Matches(CreateMessage(authorBot: true)));
    }

    [Fact]
    public void ExcludeBots_HumanAuthoredMessage_Matches()
    {
        var filter = new MessageFilter(ExcludeBots: true);

        // Bot == false and Bot == null (field absent) are both non-bots.
        Assert.True(filter.Matches(CreateMessage(authorBot: false)));
        Assert.True(filter.Matches(CreateMessage(authorBot: null)));
    }

    // --- AttachmentsOnly ---

    [Fact]
    public void AttachmentsOnly_NoAttachments_IsExcluded()
    {
        var filter = new MessageFilter(AttachmentsOnly: true);

        Assert.False(filter.Matches(CreateMessage(attachments: null)));
        Assert.False(filter.Matches(CreateMessage(attachments: new List<Attachment>())));
    }

    [Fact]
    public void AttachmentsOnly_WithAttachment_Matches()
    {
        var filter = new MessageFilter(AttachmentsOnly: true);

        Assert.True(filter.Matches(CreateMessage(attachments: new List<Attachment> { CreateAttachment() })));
    }

    // --- ContentContains ---

    [Fact]
    public void ContentContains_CaseInsensitiveSubstring_Matches()
    {
        var filter = new MessageFilter(ContentContains: "WORLD");

        Assert.True(filter.Matches(CreateMessage(content: "hello world")));
    }

    [Fact]
    public void ContentContains_NonMatchingContent_IsExcluded()
    {
        var filter = new MessageFilter(ContentContains: "missing");

        Assert.False(filter.Matches(CreateMessage(content: "hello world")));
    }

    [Fact]
    public void ContentContains_NullContent_IsExcluded()
    {
        var filter = new MessageFilter(ContentContains: "anything");

        Assert.False(filter.Matches(CreateMessage(content: null)));
    }

    // --- Since / Until ---

    [Fact]
    public void Since_MessageBeforeSince_IsExcluded()
    {
        var filter = new MessageFilter(Since: DateTimeOffset.Parse("2024-06-15T12:00:00Z"));

        Assert.False(filter.Matches(CreateMessage(timestamp: "2024-06-15T11:59:59Z")));
    }

    [Fact]
    public void Until_MessageAfterUntil_IsExcluded()
    {
        var filter = new MessageFilter(Until: DateTimeOffset.Parse("2024-06-15T12:00:00Z"));

        Assert.False(filter.Matches(CreateMessage(timestamp: "2024-06-15T12:00:01Z")));
    }

    [Fact]
    public void SinceAndUntil_MessageWithinRange_Matches()
    {
        var filter = new MessageFilter(
            Since: DateTimeOffset.Parse("2024-06-01T00:00:00Z"),
            Until: DateTimeOffset.Parse("2024-06-30T00:00:00Z"));

        Assert.True(filter.Matches(CreateMessage(timestamp: "2024-06-15T12:00:00Z")));
    }

    [Fact]
    public void Since_MessageExactlyAtSince_Matches()
    {
        // Boundary: "at or after Since" — equality is inclusive.
        var filter = new MessageFilter(Since: DateTimeOffset.Parse("2024-06-15T12:00:00Z"));

        Assert.True(filter.Matches(CreateMessage(timestamp: "2024-06-15T12:00:00Z")));
    }

    [Fact]
    public void Until_MessageExactlyAtUntil_Matches()
    {
        // Boundary: "at or before Until" — equality is inclusive.
        var filter = new MessageFilter(Until: DateTimeOffset.Parse("2024-06-15T12:00:00Z"));

        Assert.True(filter.Matches(CreateMessage(timestamp: "2024-06-15T12:00:00Z")));
    }

    // --- Combined (logical AND) ---

    [Fact]
    public void CombinedFilter_RequiresAllCriteria()
    {
        var filter = new MessageFilter(AuthorId: "100", AttachmentsOnly: true);

        var withBoth = CreateMessage(authorId: "100", attachments: new List<Attachment> { CreateAttachment() });
        var wrongAuthor = CreateMessage(authorId: "200", attachments: new List<Attachment> { CreateAttachment() });
        var noAttachments = CreateMessage(authorId: "100", attachments: null);

        Assert.True(filter.Matches(withBoth));
        Assert.False(filter.Matches(wrongAuthor));
        Assert.False(filter.Matches(noAttachments));
    }
}
