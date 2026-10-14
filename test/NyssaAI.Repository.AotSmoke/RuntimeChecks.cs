using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.AotCommands.Tasks;
using NyssaAI.Repository.AotCommands.ProjectTasks;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;

namespace NyssaAI.Repository.AotSmoke;

internal static class RuntimeChecks
{
    internal static async Task<int> RunAsync(string databaseProvider)
    {
        var connectionVariable = databaseProvider == "postgresql"
            ? "NYSSAAI_REPOSITORY_POSTGRES" : "NYSSAAI_REPOSITORY_CONNECTION";
        var connectionString = Environment.GetEnvironmentVariable(connectionVariable);
        if (databaseProvider is not ("sqlite" or "postgresql") || string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine($"FAIL configuration: a supported provider and isolated {connectionVariable} are required.");
            return 2;
        }

        try
        {
            var services = new ServiceCollection();
            if (!SmokeCheck.Completes(() => services.AddNyssaRepository(new RepositoryConfiguration
            {
                DatabaseProvider = databaseProvider,
                ConnectionString = connectionString
            }, commands: [.. TaskOperations.Register(databaseProvider).Where(command => command.Name != TaskOperations.Count.Name), .. ProjectTaskOperations.Register(databaseProvider)]))) return 1;
            services.AddNyssaRepositoryCommands(commands => commands.Add(TaskOperations.Count, parameters => new TaskCount(parameters)));
            await using (var container = services.BuildServiceProvider())
            {
                var repository = container.GetRequiredService<IRepository>();
                var provider = container.GetRequiredService<IRepositoryProvider>();
                if (await RegistryChecks.RunAsync(repository, databaseProvider).ConfigureAwait(false) != 0) return 1;
                if (await ScopeChecks.RunAsync(repository, provider).ConfigureAwait(false) != 0) return 1;
                if (await CommandPipelineChecks.RunAsync(repository, provider).ConfigureAwait(false) != 0) return 1;
                if (await ProjectTaskChecks.RunAsync(repository, provider).ConfigureAwait(false) != 0) return 1;
                return await MigrationChecks.RunAsync(databaseProvider, connectionString).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            // This executable is the terminal verification boundary; never persist driver messages or settings.
            Console.Error.WriteLine($"FAIL native verification: {exception.GetType().Name}");
            return 1;
        }
    }
}
