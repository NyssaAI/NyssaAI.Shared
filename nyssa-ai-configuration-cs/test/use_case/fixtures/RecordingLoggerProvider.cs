using Microsoft.Extensions.Logging;

namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>Records what the library logs, so a tutorial can assert on its own words.</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    public List<string> Messages { get; } = [];

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(RecordingLoggerProvider output) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (output.Messages)
                output.Messages.Add(formatter(state, exception));
        }
    }
}
