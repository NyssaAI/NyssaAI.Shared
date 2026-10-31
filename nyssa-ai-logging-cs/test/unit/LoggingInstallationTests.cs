using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NyssaAI.Logging.Configuration;
using Xunit;

namespace NyssaAI.Logging.Tests;

public sealed class LoggingInstallationTests
{
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
        finally { File.Delete(expectedPath); }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    }
}
