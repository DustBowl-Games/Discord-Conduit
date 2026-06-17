using DustBowlGames.DiscordConduit.Core.Migration;

namespace DustBowlGames.DiscordConduit.Core.Tests.Migration;

/// <summary>
/// Tests for <see cref="PauseTokenSource"/> and <see cref="PauseToken"/>, the cooperative
/// pause/resume primitive used by long-running migrations.
/// </summary>
public class PauseTokenSourceTests
{
    [Fact]
    public void DefaultToken_IsNotPaused()
    {
        var token = default(PauseToken);

        Assert.False(token.IsPaused);
    }

    [Fact]
    public async Task DefaultToken_WaitWhilePaused_CompletesImmediately()
    {
        var token = default(PauseToken);

        var task = token.WaitWhilePausedAsync(CancellationToken.None);

        Assert.True(task.IsCompleted);
        await task; // does not throw
    }

    [Fact]
    public void NewSource_IsNotPaused()
    {
        var source = new PauseTokenSource();

        Assert.False(source.IsPaused);
        Assert.False(source.Token.IsPaused);
    }

    [Fact]
    public async Task NewSource_NotPaused_WaitCompletesImmediately()
    {
        var source = new PauseTokenSource();

        var task = source.Token.WaitWhilePausedAsync(CancellationToken.None);

        Assert.True(task.IsCompleted);
        await task;
    }

    [Fact]
    public void Pause_SetsIsPausedTrue()
    {
        var source = new PauseTokenSource();

        source.Pause();

        Assert.True(source.IsPaused);
        Assert.True(source.Token.IsPaused);
    }

    [Fact]
    public async Task Pause_BlocksWait_UntilResume()
    {
        var source = new PauseTokenSource();
        source.Pause();

        var task = source.Token.WaitWhilePausedAsync(CancellationToken.None);

        // The wait must not complete while paused. Give any erroneous continuation a chance to run.
        await Task.Yield();
        Assert.False(task.IsCompleted);

        source.Resume();

        // Now it should complete. Bound the await so a regression can't hang the suite.
        await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(task.IsCompletedSuccessfully);
        Assert.False(source.IsPaused);
    }

    [Fact]
    public async Task WaitWhilePaused_AlreadyCancelledTokenWhilePaused_Throws()
    {
        var source = new PauseTokenSource();
        source.Pause();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.Token.WaitWhilePausedAsync(cts.Token));
    }
}
