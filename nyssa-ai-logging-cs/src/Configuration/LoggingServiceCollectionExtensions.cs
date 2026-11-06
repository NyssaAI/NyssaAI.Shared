using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NyssaAI.Logging.Configuration;

/// <summary>Configures an application's logging in one call, for an application that has none.</summary>
/// <remarks>
/// Returns Microsoft's logging builder so applications can chain Nyssa destinations and other
/// providers, including OpenTelemetry. The builder's Services property continues DI registration.
/// </remarks>
public static class LoggingServiceCollectionExtensions
{
    private static readonly Regex UnsafeAppNameChars = new(@"[^A-Za-z0-9._-]+", RegexOptions.Compiled);

    /// <summary>Logs to standard error, and to a rotating JSON file when one is named.</summary>
    /// <param name="services">The container to configure logging in.</param>
    /// <param name="filePath">
    /// The rotating JSON log file to write, or null for standard error only.
    /// </param>
    /// <param name="minimumLevel">The lowest level any destination emits.</param>
    /// <param name="enableOtel">When true, enables OpenTelemetry W3C <see cref="System.Diagnostics.Activity"/> span sampling and correlation.</param>
    /// <param name="packageLevels">Optional per-package / per-category minimum log levels.</param>
    /// <remarks>
    /// Standard error is always configured, so logging always has one destination that needs no
    /// filesystem. A named file is added alongside it, and a file that cannot be opened is reported
    /// on stderr and left out rather than throwing.
    /// </remarks>
    /// <returns>The logging builder for the supplied service collection.</returns>
    public static ILoggingBuilder AddNyssaLogging(
        this IServiceCollection services,
        string? filePath = null,
        LogLevel minimumLevel = LogLevel.Information,
        bool enableOtel = false,
        IReadOnlyDictionary<string, LogLevel>? packageLevels = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        ILoggingBuilder? result = null;
        services.AddLogging(builder =>
        {
            result = builder;
            builder.SetMinimumLevel(minimumLevel);
            builder.WithStdErr();

            if (packageLevels is not null)
            {
                foreach (var pair in packageLevels)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                    {
                        builder.SetPackageLevel(pair.Key, pair.Value);
                    }
                }
            }

            if (filePath is not null)
            {
                builder.AddNyssaJsonFile(filePath);
            }

            if (enableOtel)
            {
                builder.WithOtel();
            }
        });
        return result!; // AddLogging invokes its configuration callback synchronously.
    }

    /// <summary>
    /// Configures Nyssa logging with safe defaults matching Python and TypeScript:
    /// standard error output plus a dated rotating JSONL file at <c>{logDir}/{yyyy.MM.dd}-{appName}-log.json</c>.
    /// </summary>
    /// <param name="services">The container to configure logging in.</param>
    /// <param name="appName">Application name used in <c>{yyyy.MM.dd}-{appName}-log.json</c> (defaults to entry assembly name or <c>"application"</c>).</param>
    /// <param name="logDir">Directory for rotating JSON log files (defaults to <c>"./logs"</c>).</param>
    /// <param name="minimumLevel">Root minimum log level (defaults to <see cref="LogLevel.Information"/>).</param>
    /// <param name="enableOtel">When true, enables OpenTelemetry W3C <see cref="System.Diagnostics.Activity"/> span sampling and correlation.</param>
    /// <param name="packageLevels">Optional per-package / per-category minimum log levels.</param>
    /// <param name="console">When true, also echoes formatted records to standard output.</param>
    /// <returns>The logging builder for the supplied service collection.</returns>
    public static ILoggingBuilder ConfigureNyssaLogging(
        this IServiceCollection services,
        string? appName = null,
        string? logDir = null,
        LogLevel minimumLevel = LogLevel.Information,
        bool enableOtel = false,
        IReadOnlyDictionary<string, LogLevel>? packageLevels = null,
        bool console = false)
    {
        ArgumentNullException.ThrowIfNull(services);

        var resolvedApp = SanitizeAppName(appName ?? DefaultAppName());
        var resolvedDir = string.IsNullOrWhiteSpace(logDir)
            ? Path.Combine(Directory.GetCurrentDirectory(), "logs")
            : Path.GetFullPath(logDir);

        ILoggingBuilder? result = null;
        services.AddLogging(builder =>
        {
            result = builder;
            builder.SetMinimumLevel(minimumLevel);
            builder.WithStdErr();

            if (console)
            {
                builder.WithConsole();
            }

            if (packageLevels is not null)
            {
                foreach (var pair in packageLevels)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                    {
                        builder.SetPackageLevel(pair.Key, pair.Value);
                    }
                }
            }

            builder.WithRotatingJsonLog(Path.Combine(resolvedDir, $"{resolvedApp}-log.json"));

            if (enableOtel)
            {
                builder.WithOtel();
            }
        });
        return result!;
    }

    private static string DefaultAppName()
    {
        var envName = Environment.GetEnvironmentVariable("NYSSA_APP_NAME");
        if (!string.IsNullOrWhiteSpace(envName))
        {
            return envName;
        }
        return Assembly.GetEntryAssembly()?.GetName().Name ?? "application";
    }

    private static string SanitizeAppName(string rawName)
    {
        var sanitized = UnsafeAppNameChars.Replace(rawName.Trim(), "-").Trim('-', '.', '_');
        return string.IsNullOrEmpty(sanitized) ? "application" : sanitized;
    }
}

