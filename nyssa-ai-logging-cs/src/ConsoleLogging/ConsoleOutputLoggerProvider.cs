using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace NyssaAI.Logging.ConsoleLogging;

/// <summary>Uses Microsoft's console implementation with independent standard output routing.</summary>
/// <remarks>
/// Two AddConsole calls share one provider and its options. This separate registration allows
/// stdout and stderr to receive the same record without the last call changing both destinations.
/// </remarks>
[ProviderAlias("NyssaConsole")]
internal sealed class ConsoleOutputLoggerProvider : ConsoleLoggerProvider
{
    public ConsoleOutputLoggerProvider() : base(new ConsoleOutputOptions()) { }
}
