using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.AotCommands.Tasks;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Configuration;

namespace NyssaAI.Repository.AotSmoke;

internal static class RegistryChecks
{
    internal static async Task<int> RunAsync(IRepository repository, string provider)
    {
        var input = new TaskParameters { Id = 1, Title = "registry", Note = null };
        var missing = new RepoCommandKey<TaskParameters, int> { Name = "absent" };
        if (!SmokeCheck.Throws(() => repository.CommandRegistry(missing, input))) return Fail("missing-registration");
        if (!SmokeCheck.Throws(() => repository.CommandRegistry(new RepoCommandKey<TaskParameters, long> { Name = TaskOperations.Write.Name }, input)))
            return Fail("incompatible-result");
        var first = repository.CommandRegistry(TaskOperations.Write, input);
        var second = repository.CommandRegistry(TaskOperations.Write, input);
        if (ReferenceEquals(first, second) || first.Parameters != input) return Fail("fresh-command-values");
        var invalidKey = new RepoCommandKey<TaskParameters, int> { Name = "" };
        if (!SmokeCheck.Throws(() => repository.CommandRegistry(invalidKey, input))) return Fail("invalid-key");
        var validationKey = new RepoCommandKey<TaskParameters, int> { Name = "validation" };
        var validating = RepoCommandRegistration.Create(provider, validationKey,
            (TaskParameters _) => throw new ArgumentException("Invalid task."));
        var validationServices = new ServiceCollection();
        if (!SmokeCheck.Completes(() => validationServices.AddNyssaRepository(Settings(provider), commands: [validating]))) return Fail("validation-registration");
        await using (var validationContainer = validationServices.BuildServiceProvider())
        {
            var validation = SmokeCheck.Capture(() => validationContainer.GetRequiredService<IRepository>().CommandRegistry(validationKey, input));
            if (validation is not ArgumentException) return Fail("parameter-validation");
        }
        var duplicateServices = new ServiceCollection();
        var duplicate = SmokeCheck.Capture(() => duplicateServices.AddNyssaRepository(Settings(provider), commands: [validating, validating]));
        if (duplicate is not InvalidOperationException || duplicateServices.Count != 0) return Fail("duplicate-accepted");
        Console.WriteLine($"PASS {provider} registry-missing-incompatible-invalid-fresh-and-duplicate");

        var otherProvider = provider == "sqlite" ? "postgresql" : "sqlite";
        var otherServices = new ServiceCollection();
        if (!SmokeCheck.Completes(() => otherServices.AddNyssaRepository(Settings(otherProvider), commands: TaskOperations.Register(otherProvider)))) return Fail("other-provider-registration");
        await using (var otherContainer = otherServices.BuildServiceProvider())
        {
            var foreignCommand = otherContainer.GetRequiredService<IRepository>().CommandRegistry(TaskOperations.Write, input);
            var transaction = (await repository.OpenTransactionAsync());
            await using (transaction)
            {
                var rejected = await SmokeCheck.CaptureAsync(() => transaction.ExecuteAsync(foreignCommand));
                if (rejected is not ArgumentException || !await SmokeCheck.ThrowsAsync(() => transaction.CommitAsync()))
                    return Fail("provider-compatibility");
            }
            var session = (await repository.OpenSessionAsync());
            await using (session)
            {
                if (!await SmokeCheck.ThrowsAsync(() => session.ExecuteAsync(new TaskWrite(input)))) return Fail("unresolved-command-accepted");
            }
            Console.WriteLine($"PASS {provider} provider-compatibility-and-unresolved-command-rejection");
            return 0;
        }
    }

    // These alternate containers resolve commands only. No database is opened by registration or resolution.
    private static RepositoryConfiguration Settings(string provider) => new()
    {
        DatabaseProvider = provider,
        ConnectionString = provider == "sqlite" ? "Data Source=:memory:" : "Host=127.0.0.1;Database=unopened;Username=unopened;Timeout=1"
    };

    private static int Fail(string scenario)
    {
        Console.Error.WriteLine($"FAIL {scenario}");
        return 1;
    }
}
