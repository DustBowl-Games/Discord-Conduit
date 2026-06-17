using System.Text.Json;
using DustBowlGames.DiscordConduit.Core.Api.Models;

namespace DustBowlGames.DiscordConduit.Core.Tests.Api;

/// <summary>
/// Unit tests for <see cref="Poll.ToCreateRequest"/> — the projection that turns an already-sent
/// poll into a poll-create request object suitable for a webhook execute / message create body.
/// </summary>
public class PollTests
{
    private static PollMedia Media(string text) => new() { Text = text };

    private static PollAnswer Answer(string text) => new() { PollMedia = Media(text) };

    private static Poll CreatePoll(
        PollMedia? question,
        List<PollAnswer>? answers,
        bool allowMultiselect = false,
        string? expiry = null)
    {
        return new Poll
        {
            Question = question,
            Answers = answers,
            AllowMultiselect = allowMultiselect,
            Expiry = expiry
        };
    }

    /// <summary>Serializes a poll-create request object into a <see cref="JsonElement"/> for inspection.</summary>
    private static JsonElement Serialize(object request)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(request));
        return doc.RootElement.Clone();
    }

    // --- Null cases ---

    [Fact]
    public void ToCreateRequest_NullQuestion_ReturnsNull()
    {
        var poll = CreatePoll(question: null, answers: new List<PollAnswer> { Answer("Yes"), Answer("No") });

        Assert.Null(poll.ToCreateRequest());
    }

    [Fact]
    public void ToCreateRequest_NullQuestionText_ReturnsNull()
    {
        var poll = CreatePoll(question: new PollMedia { Text = null }, answers: new List<PollAnswer> { Answer("Yes") });

        Assert.Null(poll.ToCreateRequest());
    }

    [Fact]
    public void ToCreateRequest_NullAnswers_ReturnsNull()
    {
        var poll = CreatePoll(question: Media("Favorite color?"), answers: null);

        Assert.Null(poll.ToCreateRequest());
    }

    [Fact]
    public void ToCreateRequest_EmptyAnswers_ReturnsNull()
    {
        var poll = CreatePoll(question: Media("Favorite color?"), answers: new List<PollAnswer>());

        Assert.Null(poll.ToCreateRequest());
    }

    // --- Valid poll ---

    [Fact]
    public void ToCreateRequest_QuestionWithTwoAnswers_ReturnsNonNull()
    {
        var poll = CreatePoll(
            question: Media("Favorite color?"),
            answers: new List<PollAnswer> { Answer("Red"), Answer("Blue") });

        Assert.NotNull(poll.ToCreateRequest());
    }

    [Fact]
    public void ToCreateRequest_SerializedJson_ContainsQuestionAnswersAndFields()
    {
        var poll = CreatePoll(
            question: Media("Favorite color?"),
            answers: new List<PollAnswer> { Answer("Red"), Answer("Blue") });

        var request = poll.ToCreateRequest();
        Assert.NotNull(request);
        var root = Serialize(request!);

        // Question text flows through.
        Assert.Equal("Favorite color?", root.GetProperty("question").GetProperty("text").GetString());

        // Both answer texts flow through (under answers[].poll_media.text).
        var answerTexts = root.GetProperty("answers")
            .EnumerateArray()
            .Select(a => a.GetProperty("poll_media").GetProperty("text").GetString())
            .ToList();
        Assert.Contains("Red", answerTexts);
        Assert.Contains("Blue", answerTexts);

        // Required scalar fields are present.
        Assert.True(root.TryGetProperty("duration", out _));
        Assert.True(root.TryGetProperty("allow_multiselect", out _));
        Assert.True(root.TryGetProperty("layout_type", out _));

        // The raw JSON also literally contains the expected field names and texts.
        var json = JsonSerializer.Serialize(request);
        Assert.Contains("Favorite color?", json);
        Assert.Contains("Red", json);
        Assert.Contains("Blue", json);
        Assert.Contains("duration", json);
        Assert.Contains("allow_multiselect", json);
        Assert.Contains("layout_type", json);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToCreateRequest_AllowMultiselect_FlowsThrough(bool allowMultiselect)
    {
        var poll = CreatePoll(
            question: Media("Pick any"),
            answers: new List<PollAnswer> { Answer("A"), Answer("B") },
            allowMultiselect: allowMultiselect);

        var request = poll.ToCreateRequest();
        Assert.NotNull(request);
        var root = Serialize(request!);

        Assert.Equal(allowMultiselect, root.GetProperty("allow_multiselect").GetBoolean());
    }

    // --- Duration derivation ---

    [Fact]
    public void ToCreateRequest_NoExpiry_DefaultsToTwentyFourHours()
    {
        var poll = CreatePoll(
            question: Media("Q"),
            answers: new List<PollAnswer> { Answer("A") },
            expiry: null);

        var root = Serialize(poll.ToCreateRequest()!);

        Assert.Equal(24, root.GetProperty("duration").GetInt32());
    }

    [Fact]
    public void ToCreateRequest_PastExpiry_DefaultsToTwentyFourHours()
    {
        var poll = CreatePoll(
            question: Media("Q"),
            answers: new List<PollAnswer> { Answer("A") },
            expiry: DateTimeOffset.UtcNow.AddHours(-5).ToString("o"));

        var root = Serialize(poll.ToCreateRequest()!);

        Assert.Equal(24, root.GetProperty("duration").GetInt32());
    }

    [Fact]
    public void ToCreateRequest_FutureExpiry_DurationIsClampedToMax()
    {
        // Far-future expiry is clamped to Discord's 768-hour (32-day) maximum.
        var poll = CreatePoll(
            question: Media("Q"),
            answers: new List<PollAnswer> { Answer("A") },
            expiry: DateTimeOffset.UtcNow.AddDays(400).ToString("o"));

        var root = Serialize(poll.ToCreateRequest()!);

        Assert.Equal(Poll.MaxDurationHours, root.GetProperty("duration").GetInt32());
    }

    [Fact]
    public void ToCreateRequest_NearFutureExpiry_DurationWithinValidRange()
    {
        var poll = CreatePoll(
            question: Media("Q"),
            answers: new List<PollAnswer> { Answer("A") },
            expiry: DateTimeOffset.UtcNow.AddHours(48).ToString("o"));

        var duration = Serialize(poll.ToCreateRequest()!).GetProperty("duration").GetInt32();

        Assert.InRange(duration, 1, Poll.MaxDurationHours);
    }
}
