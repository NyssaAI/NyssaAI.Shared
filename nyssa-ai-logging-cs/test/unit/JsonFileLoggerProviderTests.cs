using NyssaAI.Logging.FileLogging;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NyssaAI.Logging.Configuration;
using Xunit;

namespace NyssaAI.Logging.Tests;

public sealed class JsonFileLoggerProviderTests
{
    [Fact]
    public void CreateLogger_WithAFilePath_WritesTheRecordToTheFile()
    {
        using var log = new TempLogPath();

        using (var factory = FactoryFor(log.LogPath))
        {
            factory.CreateLogger("test_app").LogInformation("Test message");
        }

        Assert.True(File.Exists(log.LogPath));
        Assert.Contains("Test message", File.ReadAllText(log.LogPath), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateLogger_WritesTheDocumentedJsonKeysInOrder()
    {
        // The key set is the contract a log shipper is built against, so it is asserted
        // exactly, and Operation comes from the event name.
        using var log = new TempLogPath();

        using (var factory = FactoryFor(log.LogPath))
        {
            factory.CreateLogger("test_app").LogInformation(new EventId(7, "Checkout"), "Test message");
        }

        var record = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(log.LogPath).Trim());

        Assert.Equal(
            new[] { "Timestamp", "Level", "Message", "Module", "Operation", "Exception", "AdditionalFields" },
            record.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("Information", record.GetProperty("Level").GetString());
        Assert.Equal("Test message", record.GetProperty("Message").GetString());
        Assert.Equal("test_app", record.GetProperty("Module").GetString());
        Assert.Equal("Checkout", record.GetProperty("Operation").GetString());
        Assert.Equal(JsonValueKind.Null, record.GetProperty("Exception").ValueKind);
        Assert.Equal(JsonValueKind.Object, record.GetProperty("AdditionalFields").ValueKind);
    }

    [Fact]
    public void CreateLogger_TakesTheModuleFromTheCategoryTheFactorySupplies()
    {
        // The category is Microsoft's, not a consumer name this package was told: a typed logger
        // names the class it serves, and that is what a reader of the file needs to see.
        using var log = new TempLogPath();

        using (var factory = FactoryFor(log.LogPath))
        {
            factory.CreateLogger<JsonFileLoggerProviderTests>().LogInformation("Test message");
        }

        var record = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(log.LogPath).Trim());
        Assert.Equal(typeof(JsonFileLoggerProviderTests).FullName, record.GetProperty("Module").GetString());
    }

    [Fact]
    public void CreateLogger_WithStructuredData_PreservesTheFieldsInTheJsonRecord()
    {
        // Fields are captured from message-template placeholders, which is how
        // Microsoft.Extensions.Logging carries structured state.
        using var log = new TempLogPath();

        using (var factory = FactoryFor(log.LogPath))
        {
            factory.CreateLogger("test_app")
                .LogInformation("Order placed {OrderId} for {UserId}", 12345, "user-789");
        }

        var record = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(log.LogPath).Trim());
        var fields = record.GetProperty("AdditionalFields");

        Assert.Equal("Order placed 12345 for user-789", record.GetProperty("Message").GetString());
        Assert.Equal(12345, fields.GetProperty("OrderId").GetInt32());
        Assert.Equal("user-789", fields.GetProperty("UserId").GetString());
        Assert.False(fields.TryGetProperty("{OriginalFormat}", out _));
    }

    [Fact]
    public void CreateLogger_WhenTheFactoryFiltersALevel_NeverReachesTheFile()
    {
        // The provider itself does not filter: the host's factory decides, exactly as it does for
        // every other provider it composes.
        using var log = new TempLogPath();

        using (var factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Warning)
            .AddNyssaJsonFile(log.LogPath)))
        {
            var logger = factory.CreateLogger("test_app");
            logger.LogDebug("Debug message");
            logger.LogInformation("Info message");
            logger.LogWarning("Warning message");
        }

        var content = File.ReadAllText(log.LogPath);
        Assert.DoesNotContain("Debug message", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Info message", content, StringComparison.Ordinal);
        Assert.Contains("Warning message", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreate_WithAPathThatHasNoDirectory_ReportsTheReasonAndCreatesNothing()
    {
        var provider = JsonFileLoggerProvider.TryCreate("relative.log", TimeProvider.System, out var error);

        Assert.Null(provider);
        Assert.NotNull(error);
        Assert.Contains("relative.log", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AddNyssaJsonFile_WhenTheFileCannotBeOpened_ReportsOnStderrAndKeepsLoggingUsable()
    {
        // An unwritable log is not a reason to fail startup, so the destination drops out and the
        // application keeps whatever else it configured.
        using var stderr = new StderrCapture();

        using var factory = LoggerFactory.Create(builder => builder.AddNyssaJsonFile("relative.log"));
        factory.CreateLogger("test_app").LogWarning("Still running");

        Assert.Contains("file logging disabled", stderr.Text, StringComparison.Ordinal);
    }

    private static ILoggerFactory FactoryFor(string path) =>
        LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddNyssaJsonFile(path));

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Information)]
    public void AddNyssaJsonFile_LowerFileMinimum_DoesNotWidenTheHostMinimum(LogLevel fileMinimum)
    {
        using var log = new TempLogPath();

        using (var factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Warning)
            .AddNyssaJsonFile(log.LogPath, fileMinimum)))
        {
            var logger = factory.CreateLogger("orders");
            logger.LogInformation("Below host minimum");
            logger.LogWarning("At host minimum");
        }

        var line = Assert.Single(File.ReadAllLines(log.LogPath));
        Assert.Contains("At host minimum", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddNyssaJsonFile_TwoDifferentMinima_EachFileRetainsItsOwnFilter(bool reverseRegistration)
    {
        using var information = new TempLogPath();
        using var errors = new TempLogPath();

        using (var factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            if (reverseRegistration)
            {
                builder.AddNyssaJsonFile(errors.LogPath, LogLevel.Error);
                builder.AddNyssaJsonFile(information.LogPath, LogLevel.Information);
            }
            else
            {
                builder.AddNyssaJsonFile(information.LogPath, LogLevel.Information);
                builder.AddNyssaJsonFile(errors.LogPath, LogLevel.Error);
            }
        }))
        {
            var logger = factory.CreateLogger("orders");
            logger.LogDebug("Hidden from both files");
            logger.LogInformation("Order accepted");
            logger.LogError("Order failed");
        }

        var informationLines = File.ReadAllLines(information.LogPath);
        Assert.Equal(2, informationLines.Length);
        Assert.Contains("Order accepted", informationLines[0], StringComparison.Ordinal);
        Assert.Contains("Order failed", informationLines[1], StringComparison.Ordinal);
        Assert.Contains("Order failed", Assert.Single(File.ReadAllLines(errors.LogPath)), StringComparison.Ordinal);
    }
}
