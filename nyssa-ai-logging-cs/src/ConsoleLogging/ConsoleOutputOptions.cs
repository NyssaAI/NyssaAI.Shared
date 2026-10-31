using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace NyssaAI.Logging.ConsoleLogging;

/// <summary>
/// Supplies a static <see cref="ConsoleLoggerOptions"/> monitor configured to write all console
/// levels to standard output rather than standard error.
/// </summary>
internal sealed class ConsoleOutputOptions : IOptionsMonitor<ConsoleLoggerOptions>
{
    public ConsoleLoggerOptions CurrentValue { get; } = new()
    {
        LogToStandardErrorThreshold = LogLevel.None,
    };

    public ConsoleLoggerOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<ConsoleLoggerOptions, string?> listener) => null;
}
