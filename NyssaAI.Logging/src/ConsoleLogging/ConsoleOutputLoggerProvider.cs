using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace NyssaAI.Logging.ConsoleLogging;

/// <summary>Uses Microsoft's console implementation with independent standard output routing.</summary>
/// <remarks>
/// Two AddConsole calls share one provider and its options. This separate registration allows
/// stdout and stderr to receive the same record without the last call changing both destinations.
/// </remarks>
[ProviderAlias("NyssaConsole")]
internal sealed class ConsoleOutputLoggerProvider : ConsoleLoggerProvider
{
    public ConsoleOutputLoggerProvider() : base(new OutputOptions()) { }

    private sealed class OutputOptions : IOptionsMonitor<ConsoleLoggerOptions>
    {
        public ConsoleLoggerOptions CurrentValue { get; } = new()
        {
            LogToStandardErrorThreshold = LogLevel.None,
        };

        public ConsoleLoggerOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<ConsoleLoggerOptions, string?> listener) => null;
    }
}
