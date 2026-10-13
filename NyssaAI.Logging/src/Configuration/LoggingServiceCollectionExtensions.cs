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
    /// <returns>The logging builder for the supplied service collection.</returns>
    public static ILoggingBuilder AddNyssaLogging(
        this IServiceCollection services,
        string? filePath = null,
        LogLevel minimumLevel = LogLevel.Information)
    {
        ArgumentNullException.ThrowIfNull(services);

        ILoggingBuilder? result = null;
        services.AddLogging(builder =>
        {
            result = builder;
            builder.SetMinimumLevel(minimumLevel);
            builder.WithStdErr();

            if (filePath is not null)
            {
                builder.AddNyssaJsonFile(filePath);
            }
        });
        return result!; // AddLogging invokes its configuration callback synchronously.
    }
}
