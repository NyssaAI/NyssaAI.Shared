using System.Diagnostics;

namespace NyssaAI.Logging.Telemetry;

/// <summary>
/// Represents an active diagnostic span backed by <see cref="Activity"/> that correlates
/// structured logs and OpenTelemetry traces.
/// </summary>
public sealed class SpanScope : IDisposable
{
    private readonly Activity? _activity;
    private readonly string _operation;
    private readonly string _traceId;
    private readonly string _spanId;
    private readonly string? _parentSpanId;
    private bool _disposed;

    internal SpanScope(Activity? activity, string name, string? operation)
    {
        _activity = activity;
        Name = name;
        _operation = string.IsNullOrWhiteSpace(operation) ? name : operation;

        if (activity is not null && activity.IdFormat == ActivityIdFormat.W3C)
        {
            _traceId = activity.TraceId.ToHexString();
            _spanId = activity.SpanId.ToHexString();
            _parentSpanId = activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString();
        }
        else
        {
            _traceId = ActivityTraceId.CreateRandom().ToHexString();
            _spanId = ActivitySpanId.CreateRandom().ToHexString();
            _parentSpanId = null;
        }
    }

    /// <summary>Gets the span name.</summary>
    public string Name { get; }

    /// <summary>Gets the operation name associated with this span.</summary>
    public string Operation => _operation;

    /// <summary>Gets the 32-character hexadecimal W3C trace identifier.</summary>
    public string TraceId => _traceId;

    /// <summary>Gets the 16-character hexadecimal W3C span identifier.</summary>
    public string SpanId => _spanId;

    /// <summary>Gets the parent span's 16-character hexadecimal identifier, or null when root.</summary>
    public string? ParentSpanId => _parentSpanId;

    /// <summary>Gets the underlying .NET <see cref="Activity"/>, when sampled.</summary>
    public Activity? Activity => _activity;

    /// <summary>Sets or updates a key-value tag on the active span.</summary>
    /// <param name="key">The attribute key.</param>
    /// <param name="value">The attribute value.</param>
    /// <returns>This <see cref="SpanScope"/> for fluent chaining.</returns>
    public SpanScope SetAttribute(string key, object? value)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            _activity?.SetTag(key, value);
        }
        return this;
    }

    /// <summary>Records an exception and sets the span status to Error.</summary>
    /// <param name="exception">The exception to record.</param>
    /// <returns>This <see cref="SpanScope"/> for fluent chaining.</returns>
    public SpanScope RecordException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (_activity is not null)
        {
            _activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            _activity.SetTag("exception.type", exception.GetType().FullName);
            _activity.SetTag("exception.message", exception.Message);
        }
        return this;
    }

    /// <summary>Marks the span status as Error with a description.</summary>
    /// <param name="description">Human-readable error description.</param>
    /// <returns>This <see cref="SpanScope"/> for fluent chaining.</returns>
    public SpanScope SetStatusError(string description)
    {
        _activity?.SetStatus(ActivityStatusCode.Error, description);
        return this;
    }

    /// <summary>Stops the underlying <see cref="Activity"/> and restores the parent span context.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _activity?.Dispose();
    }
}
