namespace DustBowlGames.DiscordConduit.Core.Tests.Fixtures;

/// <summary>
/// A fake time provider for testing time-dependent code without real delays.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly List<PendingTimer> _timers = [];

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Advances time by the given amount and fires any timers that should have triggered.</summary>
    public void Advance(TimeSpan duration)
    {
        _now += duration;

        var fired = _timers.Where(t => t.DueTime <= _now).ToList();
        foreach (var timer in fired)
        {
            _timers.Remove(timer);
            timer.Callback(timer.State);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new PendingTimer(callback, state, _now + dueTime);
        _timers.Add(timer);
        return new FakeTimer();
    }

    private sealed record PendingTimer(TimerCallback Callback, object? State, DateTimeOffset DueTime);

    private sealed class FakeTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => default;
    }
}
