using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NyssaAI.Logging.FileLogging;

namespace NyssaAI.Logging.Configuration;

/// <summary>Adds NyssaAI's rotating JSON file destination to an application's Microsoft logging.</summary>
/// <remarks>
/// An application configures logging with <c>AddLogging</c> and injects <see cref="ILogger{TCategoryName}"/>, and
/// this package supplies one destination for it to write to.
/// </remarks>
public static class LoggingBuilderExtensions
{
    /// <summary>Writes structured JSON records to a rotating file at <paramref name="filePath"/>.</summary>
    /// <param name="builder">The logging builder to add the destination to.</param>
    /// <param name="filePath">
    /// The log file to write. Its directory is created if missing and restricted to the owner; a
    /// directory that already exists keeps the permissions its operator gave it. The path must
    /// include a directory.
    /// </param>
    /// <param name="minimumLevel">
    /// The minimum level for this file after the host's filters have been applied. Each file
    /// has its own minimum. A lower file minimum cannot widen the host's filtering rules.
    /// Omit it to accept every record the host sends to the provider.
    /// </param>
    /// <remarks>
    /// A file that cannot be opened is reported on stderr and this destination is left out, rather
    /// than throwing: an unwritable log is not a reason to fail application startup. Call this more
    /// than once to write more than one file.
    /// </remarks>
    public static ILoggingBuilder AddNyssaJsonFile(
        this ILoggingBuilder builder, string filePath, LogLevel? minimumLevel = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        builder.Services.AddSingleton<ILoggerProvider>(services =>
        {
            var clock = services.GetService<TimeProvider>() ?? TimeProvider.System;
            // Keep the threshold on this instance. A framework provider-type rule would affect
            // every file and could override the host's minimum instead of narrowing it.
            var provider = JsonFileLoggerProvider.TryCreate(filePath, clock, out var error, minimumLevel);
            if (provider is not null)
            {
                return provider;
            }

            StderrDiagnostics.Error(filePath, $"file logging disabled: {error}");
            return NullLoggerProvider.Instance;
        });

        return builder;
    }
}
