using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NyssaAI.Logging.Configuration;

namespace NyssaAI.Logging.AotSmoke;

internal static class Program
{
    private static int Main()
    {
        var directory = Directory.CreateTempSubdirectory("nyssaai-logging-native-").FullName;
        var informationPath = Path.Combine(directory, "information.jsonl");
        var warningPath = Path.Combine(directory, "warning.jsonl");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information)
                .AddNyssaJsonFile(informationPath)
                .AddNyssaJsonFile(warningPath, LogLevel.Warning));
            using (var container = services.BuildServiceProvider())
            {
                var logger = container.GetRequiredService<ILoggerFactory>().CreateLogger("NativeSmoke");
                logger.LogDebug("This record must be filtered.");
                logger.LogInformation(new EventId(1, "OrderSaved"), "Saved order {OrderId}", 42);
                logger.LogWarning("Retry {Attempt}", 2);
            }

            var information = File.ReadAllLines(informationPath);
            var warnings = File.ReadAllLines(warningPath);
            if (information.Length != 2 || warnings.Length != 1)
                throw new InvalidOperationException("File-specific logging filters did not preserve the expected records.");
            using var record = JsonDocument.Parse(information[0]);
            if (record.RootElement.GetProperty("AdditionalFields").GetProperty("OrderId").GetInt32() != 42
                || record.RootElement.GetProperty("Operation").GetString() != "OrderSaved")
                throw new InvalidOperationException("The native logger did not preserve structured data.");

            Console.WriteLine("Logging native smoke passed: JSON fields, independent filters, disposal.");
            return 0;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
