using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NyssaAI.Logging.Configuration;
using NyssaAI.Logging.Telemetry;
using Xunit;

namespace NyssaAI.Logging.Tests;

public sealed class LoggingInstallationTests
{
    [Fact]
    public void ConfigureNyssaLogging_WithDefaultsAndOtel_CreatesDatedLogFileAndCorrelatesSpans()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nyssa-logging-" + Guid.NewGuid().ToString("N"));
        using var error = new StderrCapture();
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new FixedClock());
            var telemetry = services.ConfigureNyssaLogging(
                appName: "billing-worker",
                logDir: directory,
                minimumLevel: LogLevel.Information,
                enableOtel: true,
                packageLevels: new Dictionary<string, LogLevel>
                {
                    ["NyssaAI.DbGateway"] = LogLevel.Debug,
                    ["Noisy.ThirdParty"] = LogLevel.Error
                })
                .SetPackageLevel("Billing.Audit", LogLevel.Debug);

            using (var provider = services.BuildServiceProvider())
            {
                var factory = provider.GetRequiredService<ILoggerFactory>();
                var appLogger = factory.CreateLogger("Billing.Orders");
                var dbLogger = factory.CreateLogger("NyssaAI.DbGateway.Sqlite");
                var noisyLogger = factory.CreateLogger("Noisy.ThirdParty.Http");

                using (var outer = telemetry.StartSpan(
                    "checkout-order",
                    new Dictionary<string, object?> { ["OrderId"] = 1042 },
                    "OrderCheckout"))
                {
                    appLogger.LogInformation("Validating cart");
                    outer.SetAttribute("ItemCount", 3);

                    using (NyssaTelemetry.StartSpan(
                        "query-inventory",
                        new Dictionary<string, object?> { ["Sku"] = "ABC-123" },
                        "InventoryQuery"))
                    {
                        dbLogger.LogDebug("Executed inventory query");
                    }
                }

                noisyLogger.LogWarning("Dropped warning");
            }

            var expectedPath = Path.Combine(directory, "2026.10.13-billing-worker-log.json");
            Assert.True(File.Exists(expectedPath));
            var lines = File.ReadAllLines(expectedPath);
            Assert.Equal(2, lines.Length);

            using var first = JsonDocument.Parse(lines[0]);
            using var second = JsonDocument.Parse(lines[1]);

            Assert.Equal("OrderCheckout", first.RootElement.GetProperty("Operation").GetString());
            var firstFields = first.RootElement.GetProperty("AdditionalFields");
            Assert.Equal(1042, firstFields.GetProperty("OrderId").GetInt32());
            var traceId = firstFields.GetProperty("TraceId").GetString();
            var outerSpanId = firstFields.GetProperty("SpanId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(traceId));
            Assert.False(string.IsNullOrWhiteSpace(outerSpanId));

            Assert.Equal("InventoryQuery", second.RootElement.GetProperty("Operation").GetString());
            var secondFields = second.RootElement.GetProperty("AdditionalFields");
            Assert.Equal("ABC-123", secondFields.GetProperty("Sku").GetString());
            Assert.Equal(traceId, secondFields.GetProperty("TraceId").GetString());
            Assert.Equal(outerSpanId, secondFields.GetProperty("ParentSpanId").GetString());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AddNyssaLogging_ChainedDestinations_WriteOnceToEachDestination()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nyssa-logging-" + Guid.NewGuid().ToString("N"));
        var originalOutput = Console.Out;
        using var output = new StringWriter();
        using var error = new StderrCapture();
        Console.SetOut(output);
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new FixedClock());
            var builder = services.AddNyssaLogging()
                .WithRotatingJsonLog(Path.Combine(directory, "application-log.json"))
                .WithStdErr()
                .WithConsole()
                .WithStdErr()
                .WithConsole();
            Assert.Same(services, builder.Services);
            using (var provider = services.BuildServiceProvider())
            {
                provider.GetRequiredService<ILogger<LoggingInstallationTests>>()
                    .LogInformation("Chained destination {Number}", 42);
            }

            Assert.Equal(1, output.ToString().Split("Chained destination 42").Length - 1);
            Assert.Equal(1, error.Text.Split("Chained destination 42").Length - 1);
            using var record = JsonDocument.Parse(Assert.Single(File.ReadAllLines(
                Path.Combine(directory, "2026.10.13-application-log.json"))));
            Assert.Equal(42, record.RootElement.GetProperty("AdditionalFields").GetProperty("Number").GetInt32());
        }
        finally
        {
            Console.SetOut(originalOutput);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void WithRotatingJsonLog_BareFileName_WritesDatedFileInWorkingDirectory()
    {
        var fileName = "nyssa-logging-" + Guid.NewGuid().ToString("N") + ".json";
        var expectedPath = Path.Combine(Directory.GetCurrentDirectory(), "2026.10.13-" + fileName);
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new FixedClock());
            services.AddNyssaLogging().WithRotatingJsonLog(fileName, LogLevel.Warning);
            using (var provider = services.BuildServiceProvider())
            {
                var logger = provider.GetRequiredService<ILogger<LoggingInstallationTests>>();
                logger.LogInformation("Filtered");
                logger.LogWarning("Written");
            }
            using var record = JsonDocument.Parse(Assert.Single(File.ReadAllLines(expectedPath)));
            Assert.Equal("Written", record.RootElement.GetProperty("Message").GetString());
        }
        finally
        {
            if (File.Exists(expectedPath))
            {
                File.Delete(expectedPath);
            }
        }
    }
}
