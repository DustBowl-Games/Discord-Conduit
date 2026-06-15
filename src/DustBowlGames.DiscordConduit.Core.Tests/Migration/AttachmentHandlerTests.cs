using DustBowlGames.DiscordConduit.Core.Api.Models;
using DustBowlGames.DiscordConduit.Core.Migration;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Tests.Migration;

public class AttachmentHandlerTests
{
    private readonly AttachmentHandler _handler;

    public AttachmentHandlerTests()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var httpClient = new HttpClient();
        _handler = new AttachmentHandler(httpClient, logger);
    }

    private static Attachment CreateAttachment(long size, string filename = "file.txt")
    {
        return new Attachment
        {
            Id = "1",
            Filename = filename,
            Size = size,
            Url = "https://cdn.discordapp.com/attachments/1/2/file.txt"
        };
    }

    // --- IsOversized ---

    [Fact]
    public void IsOversized_AboveLimit_ReturnsTrue()
    {
        var attachment = CreateAttachment(Attachment.MaxBotUploadSize + 1);

        Assert.True(_handler.IsOversized(attachment));
    }

    [Fact]
    public void IsOversized_AtLimit_ReturnsFalse()
    {
        var attachment = CreateAttachment(Attachment.MaxBotUploadSize);

        Assert.False(_handler.IsOversized(attachment));
    }

    [Fact]
    public void IsOversized_BelowLimit_ReturnsFalse()
    {
        var attachment = CreateAttachment(1024);

        Assert.False(_handler.IsOversized(attachment));
    }

    // --- CreateMultipartContent ---

    [Fact]
    public void CreateMultipartContent_IncludesPayloadJsonField()
    {
        var files = new List<(byte[] Data, string Filename, string? ContentType)>
        {
            (new byte[] { 1, 2, 3 }, "test.png", "image/png")
        };

        var result = _handler.CreateMultipartContent("hello", "User", null, null, files);

        Assert.IsType<MultipartFormDataContent>(result);

        // Verify that payload_json is included by checking the multipart contents
        var contents = result.ToList();
        var payloadPart = contents.FirstOrDefault(c =>
            c.Headers.ContentDisposition?.Name?.Trim('"') == "payload_json");
        Assert.NotNull(payloadPart);
    }

    [Fact]
    public void CreateMultipartContent_IncludesFileAttachments()
    {
        var files = new List<(byte[] Data, string Filename, string? ContentType)>
        {
            (new byte[] { 1 }, "a.png", "image/png"),
            (new byte[] { 2 }, "b.txt", "text/plain")
        };

        var result = _handler.CreateMultipartContent(null, null, null, null, files);

        var contents = result.ToList();
        // payload_json + 2 files = 3 parts
        Assert.Equal(3, contents.Count);
    }

    [Fact]
    public async Task CreateMultipartContent_PayloadJsonContainsFields()
    {
        var files = new List<(byte[] Data, string Filename, string? ContentType)>
        {
            (new byte[] { 0 }, "f.bin", null)
        };

        var result = _handler.CreateMultipartContent("msg", "Bot", "https://avatar.url", null, files);

        var payloadPart = result.ToList().First(c =>
            c.Headers.ContentDisposition?.Name?.Trim('"') == "payload_json");
        var json = await payloadPart.ReadAsStringAsync();

        Assert.Contains("\"content\"", json);
        Assert.Contains("\"username\"", json);
        Assert.Contains("\"avatar_url\"", json);
        Assert.Contains("msg", json);
        Assert.Contains("Bot", json);
    }

    // --- Security: SSRF host allowlist ---

    [Theory]
    [InlineData("https://evil.example.com/x.txt")]
    [InlineData("https://cdn.discordapp.com.evil.com/x.txt")] // lookalike: ends with .evil.com
    [InlineData("https://notdiscordapp.com/x.txt")]           // no leading-dot subdomain match
    [InlineData("http://127.0.0.1/x.txt")]
    public async Task DownloadAttachmentAsync_NonDiscordHost_ThrowsBeforeNetwork(string url)
    {
        var attachment = new Attachment { Id = "1", Filename = "f.txt", Size = 10, Url = url };

        // The host allowlist is checked before any network call, so this fails fast.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.DownloadAttachmentAsync(attachment, CancellationToken.None));
    }

    // --- Security: mention-injection suppression ---

    [Fact]
    public async Task CreateMultipartContent_SuppressesAllMentions()
    {
        var files = new List<(byte[] Data, string Filename, string? ContentType)>
        {
            (new byte[] { 0 }, "f.bin", null)
        };

        var result = _handler.CreateMultipartContent("@everyone @here hi", "User", null, null, files);

        var payloadPart = result.ToList().First(c =>
            c.Headers.ContentDisposition?.Name?.Trim('"') == "payload_json");
        var json = await payloadPart.ReadAsStringAsync();

        // allowed_mentions with an empty parse array disables @everyone/@here/role/user pings.
        Assert.Contains("allowed_mentions", json);
        Assert.Matches("\"parse\"\\s*:\\s*\\[\\s*\\]", json);
    }

    // --- Security: filename header injection (CRLF) ---

    [Fact]
    public void CreateMultipartContent_SanitizesFilenameCrlfAndQuotes()
    {
        var files = new List<(byte[] Data, string Filename, string? ContentType)>
        {
            (new byte[] { 1 }, "a\r\nb\"c.png", "image/png")
        };

        var result = _handler.CreateMultipartContent(null, null, null, null, files);

        var filePart = result.ToList().First(c =>
            c.Headers.ContentDisposition?.Name?.Trim('"') == "files[0]");
        var fileName = filePart.Headers.ContentDisposition?.FileName ?? "";

        Assert.DoesNotContain("\r", fileName);
        Assert.DoesNotContain("\n", fileName);
    }
}
