using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NyssaAI.Logging.Configuration;

/// <summary>Configures an application's logging in one call, for an application that has none.</summary>
/// <remarks>
/// A convenience over <c>AddLogging</c>, not a replacement for it. An application that wants
/// destinations this does not configure -- a console formatter of its own, Seq, OpenTelemetry --
/// calls <c>AddLogging</c> directly and adds <see cref="LoggingBuilderExtensions.AddNyssaJsonFile"/>
/// among them.
/// </remarks>
public static class LoggingServiceCollectionExtensions
{
    /// <summary>Logs to standard error, and to a rotating JSON file when one is named.</summary>
    /// <param name="services">The container to configure logging in.</param>
    /// <param name="filePath">
    /// The rotating JSON log file to write, or null for standard error only.
    /// </param>
    /// <param name="minimumLevel">The lowest level any destination emits.</param>
    /// <remarks>
    /// Standard error is always configured, so logging always has one destination that needs no
    /// filesystem. A named file is added alongside it, and a file that cannot be opened is reported
    /// on stderr and left out rather than throwing.
    /// </remarks>
    public static IServiceCollection AddNyssaLogging(
        this IServiceCollection services,
        string? filePath = null,
        LogLevel minimumLevel = LogLevel.Information)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(minimumLevel);

            // Microsoft's console provider, pointed entirely at stderr. Diagnostics belong there:
            // it keeps stdout clean for a tool whose output is piped, and it is what a service
            // manager captures.
            builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

            if (filePath is not null)
            {
                builder.AddNyssaJsonFile(filePath);
            }
        });
    }
}
