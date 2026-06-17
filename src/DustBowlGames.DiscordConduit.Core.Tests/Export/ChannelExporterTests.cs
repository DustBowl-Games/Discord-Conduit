using System.Text.Json;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Api.Models;
using DustBowlGames.DiscordConduit.Core.Export;
using DustBowlGames.DiscordConduit.Core.Migration;
using DustBowlGames.DiscordConduit.Core.Tests.Fixtures;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Tests.Export;

public class ChannelExporterTests
{
    private static Message Msg(string id, string author, string? content,
        string timestamp = "2024-06-15T12:00:00Z", bool pinned = false, Attachment[]? attachments = null, bool bot = false)
    {
        return new Message
        {
            Id = id,
            ChannelId = "100",
            Author = new User { Id = author, Username = author, Bot = bot },
            Content = content,
            Timestamp = timestamp,
            Type = 0,
            Pinned = pinned,
            Attachments = attachments?.ToList(),
        };
    }

    private static Attachment Img(string name) => new()
    {
        Id = "a1",
        Filename = name,
        Size = 1234,
        Url = $"https://cdn.discordapp.com/attachments/1/2/{name}",
        ContentType = "image/png",
        Width = 100,
        Height = 100,
    };

    // ── JSON ──────────────────────────────────────────────────────────────

    [Fact]
    public void RenderJson_ProducesValidJsonWithMessages()
    {
        var msgs = new List<Message> { Msg("1", "alice", "hello"), Msg("2", "bob", "world") };

        var json = ChannelExporter.RenderJson("100", msgs);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("100", doc.RootElement.GetProperty("channel_id").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("message_count").GetInt32());
        var arr = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal("hello", arr[0].GetProperty("content").GetString());
        Assert.Equal("alice", arr[0].GetProperty("author").GetProperty("name").GetString());
    }

    // ── CSV ───────────────────────────────────────────────────────────────

    [Fact]
    public void RenderCsv_QuotesAndEscapesSpecialCharacters()
    {
        var msgs = new List<Message> { Msg("1", "alice", "has, comma \"quote\" and\nnewline") };

        var csv = ChannelExporter.RenderCsv(msgs);
        var lines = csv.Split('\n');

        Assert.StartsWith("timestamp,author_id,author,pinned,type,content,attachments", lines[0]);
        // The content field is quoted and internal quotes are doubled.
        Assert.Contains("\"has, comma \"\"quote\"\" and\nnewline\"", csv);
    }

    // ── Text ──────────────────────────────────────────────────────────────

    [Fact]
    public void RenderText_IncludesAuthorContentAndAttachmentUrl()
    {
        var msgs = new List<Message> { Msg("1", "alice", "hi", attachments: new[] { Img("pic.png") }) };

        var text = ChannelExporter.RenderText(msgs);

        Assert.Contains("alice", text);
        Assert.Contains("hi", text);
        Assert.Contains("pic.png", text);
        Assert.Contains("cdn.discordapp.com", text);
    }

    // ── HTML ──────────────────────────────────────────────────────────────

    [Fact]
    public void RenderHtml_EscapesMessageContent()
    {
        var msgs = new List<Message> { Msg("1", "alice", "<script>alert(1)</script> & <b>x</b>") };

        var html = ChannelExporter.RenderHtml("100", msgs);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&amp;", html);
        Assert.Contains("alice", html);
    }

    [Fact]
    public void RenderHtml_RendersImagesAsImgTags()
    {
        var msgs = new List<Message> { Msg("1", "alice", null, attachments: new[] { Img("pic.png") }) };

        var html = ChannelExporter.RenderHtml("100", msgs);

        Assert.Contains("<img src=\"https://cdn.discordapp.com/attachments/1/2/pic.png\"", html);
    }

    // ── End-to-end ExportAsync ────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_FetchesFiltersAndWritesFile()
    {
        var messages = new[]
        {
            // GetMessages returns newest-first; the exporter reverses to chronological.
            MakeMessageJson("3", "carol", "third"),
            MakeMessageJson("2", "alice", "second"),
            MakeMessageJson("1", "alice", "first"),
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, "/channels/100/messages", messages);

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(DiscordRestClient.BaseUrl) };
        var client = new DiscordRestClient("fake-token", Log.Logger, httpClient: httpClient);
        var exporter = new ChannelExporter(new MessageEndpoints(client), Log.Logger);

        var path = Path.Combine(Path.GetTempPath(), "conduit-export-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // Filter to only alice's messages.
            var count = await exporter.ExportAsync(
                "100", ExportFormat.Json, path,
                new MessageFilter(AuthorId: "alice"), progress: null, CancellationToken.None);

            Assert.Equal(2, count);
            Assert.True(File.Exists(path));

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var arr = doc.RootElement.GetProperty("messages");
            Assert.Equal(2, arr.GetArrayLength());
            // Chronological order: "first" then "second".
            Assert.Equal("first", arr[0].GetProperty("content").GetString());
            Assert.Equal("second", arr[1].GetProperty("content").GetString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static object MakeMessageJson(string id, string authorId, string content) => new
    {
        id,
        channel_id = "100",
        author = new { id = authorId, username = authorId },
        content,
        timestamp = "2024-06-15T12:00:00Z",
        type = 0,
        pinned = false,
        attachments = Array.Empty<object>(),
    };
}
