using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NyssaAI.Logging.Configuration;
using NyssaAI.Logging.UseCaseTests.fixtures;
using NyssaAI.Logging.UseCaseTests.domain;
using Xunit;

namespace NyssaAI.Logging.UseCaseTests;

/// <summary>Send an application's logging to a rotating JSON file it can search later.</summary>
public sealed class LoggingTutorials
{
    /// <summary>Add Nyssa's JSON file to the logging an application already configures.
    /// There is no Nyssa logger to resolve: a class asks for ILogger&lt;T&gt; as it always would,
    /// and this package is one more destination the host writes to.
    /// </summary>
    [Fact]
    public void Write_Logs_To_A_Json_File()
    {
        // Arrange
        // Configure Microsoft logging as usual, and name a file for Nyssa to write
        using var workspace = new LogWorkspace();
        var services = new ServiceCollection()
            .AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Information)
                .AddNyssaJsonFile(workspace.LogPath))
            .AddScoped<OrderService>();

        // Act
        using (var provider = ServiceContainer.BuildValidated(services))
        using (var scope = provider.Services.CreateScope())
        {
            // The class takes the framework's own logger; nothing in it names Nyssa
            scope.ServiceProvider.GetRequiredService<OrderService>().Place(1042);
        }

        // Assert
        // The container is closed, so the file is flushed and can be read
        var record = Assert.Single(workspace.ReadRecords());
        Assert.Equal("Information", record.GetProperty("Level").GetString());
        Assert.Equal("Order 1042 placed", record.GetProperty("Message").GetString());
        // Module is the logger's category, which for ILogger<OrderService> names the class
        Assert.Equal(typeof(OrderService).FullName, record.GetProperty("Module").GetString());
    }


    /// <summary>Configure logging in one call, for an application that configures none of its own.
    /// Standard error is always set up, so logging always has a destination that needs no
    /// filesystem; naming a file adds the rotating JSON log alongside it. An application that wants
    /// destinations this does not cover calls AddLogging directly and adds AddNyssaJsonFile among
    /// them, which is what the tutorials above do.
    /// </summary>
    [Fact]
    public void Configure_Logging_In_One_Call()
    {
        // Arrange
        using var workspace = new LogWorkspace();
        var services = new ServiceCollection()
            .AddNyssaLogging(workspace.LogPath, LogLevel.Information)
            .AddScoped<OrderService>();

        // Act
        using (var provider = ServiceContainer.BuildValidated(services))
        using (var scope = provider.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<OrderService>().Place(1042);
        }

        // Assert
        // The record reached the file; it reached standard error too
        var record = Assert.Single(workspace.ReadRecords());
        Assert.Equal("Order 1042 placed", record.GetProperty("Message").GetString());
    }


    /// <summary>Log to standard error alone, writing nothing to disk.
    /// Omit the file path for an interactive tool, a container that ships its stderr elsewhere, or
    /// a test: logging is configured and nothing touches the filesystem.
    /// </summary>
    [Fact]
    public void Log_To_Standard_Error_Only()
    {
        // Arrange
        using var workspace = new LogWorkspace();
        var services = new ServiceCollection().AddNyssaLogging(minimumLevel: LogLevel.Information);
        bool informationEnabled;
        bool debugEnabled;

        // Act
        using (var provider = ServiceContainer.BuildValidated(services))
        {
            var logger = provider.Services.GetRequiredService<ILogger<LoggingTutorials>>();
            logger.LogInformation("Inventory checked for {Sku}", "ABC-123");

            informationEnabled = logger.IsEnabled(LogLevel.Information);
            debugEnabled = logger.IsEnabled(LogLevel.Debug);
        }

        // Assert
        Assert.True(informationEnabled);
        Assert.False(debugEnabled);

        // No file was named, so none was created
        Assert.False(File.Exists(workspace.LogPath));
    }


    /// <summary>Log a message whose values become searchable fields instead of text.
    /// Name the event with an EventId and put values in {Placeholders} rather than string-formatting
    /// them in. Nyssa writes the event name as Operation and each placeholder as a typed entry in
    /// AdditionalFields, so a log search can filter on OrderId = 1042 instead of pattern-matching
    /// the message.
    /// </summary>
    [Fact]
    public void Log_Structured_Messages()
    {
        // Arrange
        using var workspace = new LogWorkspace();
        var services = new ServiceCollection().AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddNyssaJsonFile(workspace.LogPath));

        // Act
        using (var provider = ServiceContainer.BuildValidated(services))
        {
            var logger = provider.Services.GetRequiredService<ILogger<LoggingTutorials>>();

            // Name the event, then give every searchable value its own {Placeholder}.
            // Pass the values, not the object: an object as a single placeholder is written as text.
            logger.LogInformation(
                new EventId(10, "OrderAccepted"),
                "Order {OrderId} accepted for {Amount}, shipping {Shipping} to {City}, {Region} {PostalCode}",
                1042,
                39.50m,
                ShippingService.Express,
                "Seattle",
                "WA",
                "98101");
        }

        // Assert
        var record = Assert.Single(workspace.ReadRecords());
        Assert.Equal("OrderAccepted", record.GetProperty("Operation").GetString());
        var fields = record.GetProperty("AdditionalFields");
        Assert.Equal(1042, fields.GetProperty("OrderId").GetInt32());        // int stays a number
        Assert.Equal(39.50m, fields.GetProperty("Amount").GetDecimal());     // decimal stays a number
        Assert.Equal("Express", fields.GetProperty("Shipping").GetString()); // enum is written as its name
        Assert.Equal("Seattle", fields.GetProperty("City").GetString());     // each address part is its own field
        Assert.Equal("98101", fields.GetProperty("PostalCode").GetString());
    }


    /// <summary>Choose which records reach the file.
    /// The host's minimum level applies to every destination it composes, and AddFilter narrows one
    /// destination further. Here the console keeps everything from Information up while the file
    /// takes only Warning and above, so a shipped log stays small without silencing the terminal.
    /// </summary>
    [Fact]
    public void Choose_Which_Records_Reach_The_File()
    {
        // Arrange
        using var workspace = new LogWorkspace();
        var services = new ServiceCollection().AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddNyssaJsonFile(workspace.LogPath, LogLevel.Warning));

        // Act
        using (var provider = ServiceContainer.BuildValidated(services))
        {
            var logger = provider.Services.GetRequiredService<ILogger<LoggingTutorials>>();
            logger.LogInformation("Inventory checked for {Sku}", "ABC-123");  // below the file's filter
            logger.LogWarning("Inventory low for {Sku}", "ABC-123");
        }

        // Assert
        var record = Assert.Single(workspace.ReadRecords());
        Assert.Equal("Inventory low for ABC-123", record.GetProperty("Message").GetString());
    }


    /// <summary>Keep running when a log file cannot be opened.
    /// A path with no directory, a read-only volume, a full disk: that destination drops out, the
    /// reason goes to stderr, and the application starts and logs normally through everything else
    /// it configured. An unwritable log is not a reason to fail startup.
    /// </summary>
    [Fact]
    public void Keep_Running_When_The_Log_File_Cannot_Be_Opened()
    {
        // Arrange
        // Two destinations: one usable, and "relative.log", which names no directory
        using var workspace = new LogWorkspace();
        var services = new ServiceCollection().AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddNyssaJsonFile("relative.log")
            .AddNyssaJsonFile(workspace.LogPath));

        // Act
        // Composition succeeds: the unusable file is reported on stderr and left out
        using (var provider = ServiceContainer.BuildValidated(services))
        {
            provider.Services.GetRequiredService<ILogger<LoggingTutorials>>().LogWarning("Still running");
        }

        // Assert
        // The destination that worked received the record
        var record = Assert.Single(workspace.ReadRecords());
        Assert.Equal("Still running", record.GetProperty("Message").GetString());
    }


    private enum ShippingService
    {
        Ground,
        Express,
    }
}
