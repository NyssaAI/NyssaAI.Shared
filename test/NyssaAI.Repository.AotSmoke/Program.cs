using System.Diagnostics;
using NyssaAI.Repository.UseCaseTests.fixtures;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Results;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using ConfigurationStore = NyssaAI.Configuration.IConfiguration;

namespace NyssaAI.Repository.AotSmoke;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var timer = Stopwatch.StartNew();
        var allocated = GC.GetTotalAllocatedBytes();
        try
        {
            if (args.Length == 2 && args[0] == "verify")
                return await RuntimeChecks.RunAsync(args[1]).ConfigureAwait(false);
            if (args.Length < 2 || args[0] is not ("setup" or "run" or "switch"))
            {
                Console.Error.WriteLine("Usage: setup <directory> <provider> | run <directory> | switch <directory>");
                return 2;
            }
            var encodedKey = Environment.GetEnvironmentVariable("NYSSAAI_REPOSITORY_KEY");
            if (string.IsNullOrWhiteSpace(encodedKey))
                return Fail("A valid smoke-test encryption key is required.");
            using var configurationHost = new ConfigurationHost(encodedKey);
            ConfigurationStore configuration = configurationHost.Open(args[1]);
            if (args[0] == "setup")
            {
                if (args.Length != 3)
                    return 2;
                var saved = await configuration.SetRepositoryConfigurationAsync(new RepositoryConfiguration
                {
                    DatabaseProvider = args[2],
                    ConnectionString = Environment.GetEnvironmentVariable(args[2] == "postgresql"
                        ? "NYSSAAI_REPOSITORY_POSTGRES" : "NYSSAAI_REPOSITORY_CONNECTION") ?? string.Empty
                }, storageProvider: "aesLocal").ConfigureAwait(false);
                if (saved.IsFailure)
                    return Fail("Settings were rejected.");
                Console.WriteLine("Protected settings saved.");
                return 0;
            }
            if (args[0] == "switch")
                return await SwitchAsync(configuration).ConfigureAwait(false);
            return await RunAsync(configuration).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or ResultException or InvalidOperationException)
        {
            return Fail("The smoke-test environment could not be used.");
        }
        finally
        {
            using var process = Process.GetCurrentProcess();
            Console.WriteLine($"elapsed_main_ms={timer.Elapsed.TotalMilliseconds:F3}; allocated_bytes={GC.GetTotalAllocatedBytes() - allocated}; working_set_bytes={process.WorkingSet64}");
        }
    }

    private static async Task<int> RunAsync(ConfigurationStore configuration)
    {
        var services = new ServiceCollection();
        await services.AddNyssaRepositoryAsync(configuration).ConfigureAwait(false);
        await using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<IRepositoryProvider>();
        return await CheckAsync(provider).ConfigureAwait(false);
    }

    private static async Task<int> SwitchAsync(ConfigurationStore configuration)
    {
        var saved = await configuration.SetRepositoryConfigurationAsync(new RepositoryConfiguration
        {
            DatabaseProvider = "sqlite", ConnectionString = "Data Source=:memory:"
        }, storageProvider: "aesLocal").ConfigureAwait(false);
        if (saved.IsFailure)
            return Fail("Initial protected settings could not be saved.");
        var firstSettings = await configuration.GetRepositoryConfigurationAsync().ConfigureAwait(false);
        if (firstSettings.IsFailure)
            return Fail("Initial settings could not be loaded.");
        var firstServices = new ServiceCollection();
        firstServices.AddNyssaRepository(firstSettings.Value);
        await using var firstContainer = firstServices.BuildServiceProvider();
        var first = firstContainer.GetRequiredService<IRepositoryProvider>();
        var updated = await configuration.SetRepositoryConfigurationAsync(new RepositoryConfiguration
        {
            DatabaseProvider = "postgresql",
            ConnectionString = Environment.GetEnvironmentVariable("NYSSAAI_REPOSITORY_POSTGRES") ?? string.Empty
        }).ConfigureAwait(false);
        if (updated.IsFailure)
            return Fail("Replacement settings could not be saved.");
        var secondSettings = await configuration.GetRepositoryConfigurationAsync().ConfigureAwait(false);
        if (secondSettings.IsFailure)
            return Fail("Replacement settings could not be loaded.");
        var secondServices = new ServiceCollection();
        secondServices.AddNyssaRepository(() => secondSettings.Value);
        await using var secondContainer = secondServices.BuildServiceProvider();
        var second = secondContainer.GetRequiredService<IRepositoryProvider>();
        if (first.DatabaseProvider != "sqlite" || second.DatabaseProvider != "postgresql")
            return Fail("Provider snapshots were not preserved.");
        if (await CheckAsync(first).ConfigureAwait(false) != 0 || await CheckAsync(second).ConfigureAwait(false) != 0)
            return 1;
        Console.WriteLine("Stored selection changed; existing provider kept its snapshot.");
        return 0;
    }

    private static async Task<int> CheckAsync(IRepositoryProvider provider)
    {
        var opened = await provider.OpenConnectionAsync().ConfigureAwait(false);
        if (opened.IsFailure)
            return Fail("The selected database could not be opened.");
        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        if (Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false)) != 1)
            return Fail("The database check failed.");
        Console.WriteLine($"provider={provider.DatabaseProvider}; query=passed");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
