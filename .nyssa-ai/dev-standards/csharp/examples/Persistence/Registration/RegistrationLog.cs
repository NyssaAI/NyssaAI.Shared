using System.Text.Json;
using System.Text.RegularExpressions;

namespace Standards.Sample.Persistence.Registration;

/// <summary>Serializes allowlisted registration diagnostics into a borrowed host sink.</summary>
/// <remarks>The host owns levels, retention and flushing. The sink must return promptly; a blocking callback cannot be interrupted.</remarks>
public sealed class RegistrationLog
{
    private readonly Action<string> _sink;
    private readonly string _version;
    private long _lostRecords;

    /// <summary>Accepts a synchronous nonblocking sink and a bounded deployment label; invalid setup throws.</summary>
    public RegistrationLog(Action<string> sink, string version)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (!Regex.IsMatch(version, @"\A[a-zA-Z0-9._-]{1,64}\z"))
            throw new ArgumentException("Use a bounded deployment label.", nameof(version));
        _sink = sink;
        _version = version;
    }

    /// <summary>Independent cumulative sink-loss count; inspect even when the sink is unavailable.</summary>
    public long LostRecords => Interlocked.Read(ref _lostRecords);

    internal void Completed(Guid correlationId, string outcome, double elapsedMs, string code, int attempts)
    {
        Write("registration.completed", outcome == "failure" ? "error" : "info", correlationId,
            outcome, elapsedMs, code, attempts);
    }

    internal void Slow(Guid correlationId, double elapsedMs)
    {
        Write("registration.slow", "warn", correlationId, "degraded", elapsedMs, "dependency.slow", 1);
    }

    private void Write(string eventName, string level, Guid correlationId, string outcome,
        double elapsedMs, string code, int attempts)
    {
        try
        {
            _sink(JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.UtcNow, @event = eventName, level,
                service = "registration-example", version = _version, operation = "email.register",
                correlation_id = correlationId, outcome, elapsed_ms = elapsedMs,
                dependency = "postgres", attempts, code
            }) + "\n");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Only telemetry is contained; never recursively log into this sink.
            Interlocked.Increment(ref _lostRecords);
        }
    }
}
