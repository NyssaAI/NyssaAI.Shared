using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.AotCommands.Tasks;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Configuration;
using NyssaAI.Results;

namespace NyssaAI.Repository.AotSmoke;

internal static class RegistryChecks
{
    internal static async Task<int> RunAsync(IRepository repository, string provider)
    {
        var input = new TaskParameters { Id = 1, Title = "registry", Note = null };
        var missing = new RepoCommandKey<TaskParameters, int> { Name = "absent" };
        if (repository.CommandRegistry(missing, input).IsSuccess) return Fail("missing-registration");
        if (repository.CommandRegistry(new RepoCommandKey<TaskParameters, long> { Name = TaskOperations.Write.Name }, input).IsSuccess)
            return Fail("incompatible-result");
        var first = repository.CommandRegistry(TaskOperations.Write, input).ThrowIfError();
        var second = repository.CommandRegistry(TaskOperations.Write, input).ThrowIfError();
        if (ReferenceEquals(first, second) || first.Parameters != input) return Fail("fresh-command-values");
        var invalidKey = new RepoCommandKey<TaskParameters, int> { Name = "" };
        if (repository.CommandRegistry(invalidKey, input).IsSuccess) return Fail("invalid-key");
        var validationKey = new RepoCommandKey<TaskParameters, int> { Name = "validation" };
        var validating = RepoCommandRegistration.Create(provider, validationKey,
            (TaskParameters _) => Result<IRepoCommand<TaskParameters, int>>.Failure(Error.FromMessage("Invalid task.", ErrorCategory.Validation))).ThrowIfError();
        var validationServices = new ServiceCollection();
        if (validationServices.AddNyssaRepository(Settings(provider), commands: [validating]).IsFailure) return Fail("validation-registration");
        await using (var validationContainer = validationServices.BuildServiceProvider())
        {
            var validation = validationContainer.GetRequiredService<IRepository>().CommandRegistry(validationKey, input);
            if (validation.IsSuccess || validation.Errors[0].Category != ErrorCategory.Validation) return Fail("parameter-validation");
        }
        var duplicateServices = new ServiceCollection();
        if (duplicateServices.AddNyssaRepository(Settings(provider), commands: [validating, validating]).IsFailure) return Fail("duplicate-registration");
        await using (var duplicateContainer = duplicateServices.BuildServiceProvider())
        {
            var duplicate = duplicateContainer.GetRequiredService<IRepository>().CommandRegistry(validationKey, input);
            if (duplicate.IsSuccess || duplicate.Errors[0].Category != ErrorCategory.Conflict) return Fail("duplicate-accepted");
        }
        Console.WriteLine($"PASS {provider} registry-missing-incompatible-invalid-fresh-and-duplicate");

        var otherProvider = provider == "sqlite" ? "postgresql" : "sqlite";
        var otherServices = new ServiceCollection();
        if (otherServices.AddNyssaRepository(Settings(otherProvider), commands: TaskOperations.Register(otherProvider)).IsFailure) return Fail("other-provider-registration");
        await using var otherContainer = otherServices.BuildServiceProvider();
        var foreignCommand = otherContainer.GetRequiredService<IRepository>().CommandRegistry(TaskOperations.Write, input).ThrowIfError();
        var transaction = (await repository.OpenTransactionAsync()).ThrowIfError();
        await using (transaction)
        {
            var rejected = await transaction.ExecuteAsync(foreignCommand);
            if (rejected.IsSuccess || rejected.Errors[0].Category != ErrorCategory.Validation || (await transaction.CommitAsync()).IsSuccess)
                return Fail("provider-compatibility");
        }
        var session = (await repository.OpenSessionAsync()).ThrowIfError();
        await using (session)
        {
            if ((await session.ExecuteAsync(new TaskWrite(input))).IsSuccess) return Fail("unresolved-command-accepted");
        }
        Console.WriteLine($"PASS {provider} provider-compatibility-and-unresolved-command-rejection");
        return 0;
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
