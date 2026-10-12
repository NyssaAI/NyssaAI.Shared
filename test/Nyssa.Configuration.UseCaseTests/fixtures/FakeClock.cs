namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>A clock a tutorial can move, so token expiry is a decision rather than a wait.</summary>
public sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
