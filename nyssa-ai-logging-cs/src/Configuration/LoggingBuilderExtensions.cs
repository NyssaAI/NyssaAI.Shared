using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NyssaAI.Logging.ConsoleLogging;
using NyssaAI.Logging.FileLogging;

namespace NyssaAI.Logging.Configuration;

/// <summary>Adds file, standard error, and standard output destinations to Microsoft logging.</summary>
/// <remarks>
/// An application configures logging with <c>AddLogging</c> and injects <see cref="ILogger{TCategoryName}"/>, and
/// this package supplies destinations for it to write to.
/// </remarks>
public static class LoggingBuilderExtensions
{
    /// <summary>Adds standard error output. Repeated calls keep a single destination.</summary>
    /// <param name="builder">The logging builder to configure.</param>
    /// <returns>The same builder for further logging setup.</returns>
    public static ILoggingBuilder WithStdErr(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    }

    /// <summary>Adds standard output alongside other destinations, including standard error.</summary>
    /// <param name="builder">The logging builder to configure.</param>
    /// <returns>The same builder for further logging setup.</returns>
    /// <remarks>Repeated calls keep a single standard output destination.</remarks>
    public static ILoggingBuilder WithConsole(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, ConsoleOutputLoggerProvider>());
        return builder;
    }

    /// <summary>Adds a rotating JSON file whose name starts with its opening date in UTC.</summary>
    /// <param name="builder">The logging builder to configure.</param>
    /// <param name="filePath">A file name or path. Relative paths use the working directory.</param>
    /// <param name="minimumLevel">An optional minimum for this file after host filtering.</param>
    /// <returns>The same builder for further logging setup.</returns>
    /// <remarks>
    /// The name is yyyy.MM.dd followed by a hyphen and the supplied file name. The date is chosen
    /// when the provider opens; size rotation retains that name for the provider's lifetime.
    /// </remarks>
    public static ILoggingBuilder WithRotatingJsonLog(
        this ILoggingBuilder builder, string filePath, LogLevel? minimumLevel = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (string.IsNullOrEmpty(Path.GetFileName(filePath)))
        {
            throw new ArgumentException("The path must name a log file.", nameof(filePath));
        }
        return AddFile(builder, filePath, minimumLevel, dated: true);
    }

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
        return AddFile(builder, filePath, minimumLevel, dated: false);
    }

    private static ILoggingBuilder AddFile(ILoggingBuilder builder, string filePath, LogLevel? minimumLevel, bool dated)
    {
        builder.Services.AddSingleton<ILoggerProvider>(services =>
        {
            var clock = services.GetService<TimeProvider>() ?? TimeProvider.System;
            var destination = filePath;
            if (dated)
            {
                var directory = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(directory)) directory = Directory.GetCurrentDirectory();
                var date = clock.GetUtcNow().ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
                destination = Path.Combine(directory, $"{date}-{Path.GetFileName(filePath)}");
            }
            // Keep the threshold on this instance. A framework provider-type rule would affect
            // every file and could override the host's minimum instead of narrowing it.
            var provider = JsonFileLoggerProvider.TryCreate(destination, clock, out var error, minimumLevel);
            if (provider is not null)
            {
                return provider;
            }

            StderrDiagnostics.Error(destination, $"file logging disabled: {error}");
            return NullLoggerProvider.Instance;
        });

        return builder;
    }
}
