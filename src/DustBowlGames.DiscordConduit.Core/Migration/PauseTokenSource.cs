namespace DustBowlGames.DiscordConduit.Core.Migration;

/// <summary>
/// A lightweight pause primitive that lets a long-running operation be suspended and resumed
/// cooperatively. The controlling code calls <see cref="Pause"/> and <see cref="Resume"/>; the
/// running code awaits <see cref="PauseToken.WaitWhilePausedAsync"/> at a safe point.
/// </summary>
public sealed class PauseTokenSource
{
    private readonly object _lock = new();

    // null when not paused; a pending completion source when paused.
    private TaskCompletionSource<bool>? _paused;

    /// <summary>
    /// Gets a value indicating whether the source is currently paused.
    /// </summary>
    public bool IsPaused
    {
        get
        {
            lock (_lock)
            {
                return _paused is not null;
            }
        }
    }

    /// <summary>
    /// Gets a <see cref="PauseToken"/> linked to this source that running code can await.
    /// </summary>
    public PauseToken Token => new(this);

    /// <summary>
    /// Transitions the source into the paused state. Code awaiting the token will block at the
    /// next pause point until <see cref="Resume"/> is called. Calling this while already paused
    /// has no effect.
    /// </summary>
    public void Pause()
    {
        lock (_lock)
        {
            _paused ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>
    /// Resumes the source, releasing any code currently waiting on the token. Calling this while
    /// not paused has no effect.
    /// </summary>
    public void Resume()
    {
        TaskCompletionSource<bool>? toComplete;
        lock (_lock)
        {
            toComplete = _paused;
            _paused = null;
        }

        toComplete?.TrySetResult(true);
    }

    /// <summary>
    /// Returns a task that completes immediately if not paused, or when <see cref="Resume"/> is
    /// called if currently paused. Internal so only <see cref="PauseToken"/> consumes it.
    /// </summary>
    /// <param name="ct">A cancellation token observed while waiting for resumption.</param>
    /// <returns>A task that completes when the source is not paused.</returns>
    internal Task WaitWhilePausedAsync(CancellationToken ct)
    {
        TaskCompletionSource<bool>? paused;
        lock (_lock)
        {
            paused = _paused;
        }

        if (paused is null)
        {
            return ct.IsCancellationRequested ? Task.FromCanceled(ct) : Task.CompletedTask;
        }

        if (!ct.CanBeCanceled)
        {
            return paused.Task;
        }

        return AwaitWithCancellationAsync(paused.Task, ct);
    }

    private static async Task AwaitWithCancellationAsync(Task pausedTask, CancellationToken ct)
    {
        var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancelTcs))
        {
            var completed = await Task.WhenAny(pausedTask, cancelTcs.Task).ConfigureAwait(false);
            if (completed != pausedTask)
            {
                ct.ThrowIfCancellationRequested();
            }
        }

        await pausedTask.ConfigureAwait(false);
    }
}

/// <summary>
/// A token that observes a <see cref="PauseTokenSource"/>. A default token is never paused and
/// behaves as a no-op. Pass one into a long-running operation and await
/// <see cref="WaitWhilePausedAsync"/> at safe suspension points.
/// </summary>
public readonly struct PauseToken
{
    private readonly PauseTokenSource? _source;

    /// <summary>
    /// Creates a token bound to the given source.
    /// </summary>
    /// <param name="source">The source this token observes.</param>
    internal PauseToken(PauseTokenSource source)
    {
        _source = source;
    }

    /// <summary>
    /// Gets a value indicating whether the underlying source is currently paused. A default token
    /// is never paused.
    /// </summary>
    public bool IsPaused => _source?.IsPaused ?? false;

    /// <summary>
    /// Returns a task that completes immediately when not paused (including for a default token),
    /// or when the underlying source is resumed if it is currently paused.
    /// </summary>
    /// <param name="ct">A cancellation token observed while waiting for resumption.</param>
    /// <returns>A task that completes once the operation may proceed.</returns>
    public Task WaitWhilePausedAsync(CancellationToken ct)
    {
        if (_source is null)
        {
            return ct.IsCancellationRequested ? Task.FromCanceled(ct) : Task.CompletedTask;
        }

        return _source.WaitWhilePausedAsync(ct);
    }
}
