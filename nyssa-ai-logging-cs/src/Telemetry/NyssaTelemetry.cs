using System.Diagnostics;

namespace NyssaAI.Logging.Telemetry;

/// <summary>
/// Programmatic OpenTelemetry / <see cref="Activity"/> span helper for correlating Nyssa structured logs.
/// </summary>
public static class NyssaTelemetry
{
    /// <summary>The canonical <see cref="ActivitySource"/> name emitted by Nyssa logging spans.</summary>
    public const string SourceName = "NyssaAI.Logging";

    private static readonly ActivitySource Source = new(SourceName, "0.2.3");
    private static readonly object SyncRoot = new();
    private static ActivityListener? _defaultListener;

    /// <summary>
    /// Ensures an in-process <see cref="ActivityListener"/> is registered so <see cref="StartSpan"/>
    /// always produces W3C <c>TraceId</c> and <c>SpanId</c> values even when no external OpenTelemetry collector is attached.
    /// </summary>
    public static void EnsureListenerRegistered()
    {
        if (_defaultListener is not null)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_defaultListener is not null)
            {
                return;
            }

            var listener = new ActivityListener
            {
                ShouldListenTo = source => string.Equals(source.Name, SourceName, StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            };
            ActivitySource.AddActivityListener(listener);
            _defaultListener = listener;
        }
    }

    /// <summary>
    /// Starts an active span on the <c>NyssaAI.Logging</c> <see cref="ActivitySource"/> and attaches optional attributes.
    /// </summary>
    /// <param name="name">The span name.</param>
    /// <param name="attributes">Optional initial key-value tags attached to the span and inherited by nested logs.</param>
    /// <param name="operation">Optional logical operation name (defaults to <paramref name="name"/>).</param>
    /// <returns>A disposable <see cref="SpanScope"/> that ends the span when disposed.</returns>
    public static SpanScope StartSpan(
        string name,
        IReadOnlyDictionary<string, object?>? attributes = null,
        string? operation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureListenerRegistered();

        var activityName = string.IsNullOrWhiteSpace(operation) ? name : operation;
        var activity = Source.StartActivity(activityName, ActivityKind.Internal);
        if (activity is not null && attributes is not null)
        {
            foreach (var pair in attributes)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    activity.SetTag(pair.Key, pair.Value);
                }
            }
        }

        return new SpanScope(activity, name, operation);
    }
}
