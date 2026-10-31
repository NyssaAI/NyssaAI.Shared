namespace NyssaAI.Logging.Tests;

internal sealed class FixedClock : TimeProvider
{
    private DateTimeOffset _now;

    public FixedClock() : this(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero))
    {
    }

    public FixedClock(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
