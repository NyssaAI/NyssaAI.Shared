using NyssaAI.Repository.AotCommands.Tasks;
using NyssaAI.Repository.CommandPipelines;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Providers;
using NyssaAI.Results;

namespace NyssaAI.Repository.AotSmoke;

internal static class CommandPipelineChecks
{
    internal static async Task<int> RunAsync(IRepository repository, IRepositoryProvider provider)
    {
        var setup = (await provider.OpenConnectionAsync()).ThrowIfError();
        await using (setup)
        {
            await using var ddl = setup.CreateCommand();
            ddl.CommandText = "DROP TABLE IF EXISTS f002_outbox; CREATE TABLE f002_outbox (task_id BIGINT PRIMARY KEY, title TEXT NOT NULL)";
            await ddl.ExecuteNonQueryAsync();
        }
        var input = new TaskParameters { Id = 100, Title = "fluent-outbox", Note = null };
        var transaction = (await repository.OpenTransactionAsync()).ThrowIfError();
        await using (transaction)
        {
            var result = await transaction.Pipeline(repository, TaskOperations.Write, input)
                .Then(TaskOperations.Find, affected => affected == 1 ? Result<TaskParameters>.Success(input) : InvalidInput())
                .Then(TaskOperations.Outbox, row => input with { Id = row.Id, Title = row.Title }).ExecuteAsync();
            if (!result.TryGetValue(out var affected) || affected != 1 || (await transaction.CommitAsync()).IsFailure) return Fail("pipeline-outbox");
        }
        var imperative = (await repository.OpenTransactionAsync()).ThrowIfError();
        await using (imperative)
        {
            var values = input with { Id = 101, Title = "imperative-outbox" };
            var written = await imperative.ExecuteAsync(repository.CommandRegistry(TaskOperations.Write, values).ThrowIfError());
            if (!written.TryGetValue(out var affected) || affected != 1) return Fail("imperative-write");
            var found = await imperative.ExecuteAsync(repository.CommandRegistry(TaskOperations.Find, values).ThrowIfError());
            if (!found.TryGetValue(out var row)) return Fail("imperative-find");
            var notified = await imperative.ExecuteAsync(repository.CommandRegistry(TaskOperations.Outbox, values with { Id = row.Id, Title = row.Title }).ThrowIfError());
            if (!notified.TryGetValue(out var count) || count != 1 || (await imperative.CommitAsync()).IsFailure) return Fail("imperative-outbox");
        }
        if (!await CountAsync(provider, "SELECT COUNT(*) FROM f002_tasks t INNER JOIN f002_outbox o ON t.id = o.task_id AND t.title = o.title", 2)) return Fail("outbox-persistence");
        Pass(provider, "pipeline-typed-dependencies-imperative-parity-atomic-outbox");

        foreach (var mode in new[] { "resolution", "factory", "cancellation" })
        {
            var failed = (await repository.OpenTransactionAsync()).ThrowIfError();
            await using (failed)
            using (var cancellation = new CancellationTokenSource())
            {
                var values = input with { Id = 200, Title = mode };
                var missing = new RepoCommandKey<TaskParameters, TaskRow> { Name = "missing-after-write" };
                var next = mode == "resolution" ? missing : TaskOperations.Find;
                var downstreamStarted = false;
                var result = await failed.Pipeline(repository, TaskOperations.Write, values)
                    .Then(next, _ =>
                    {
                        if (mode == "cancellation") cancellation.Cancel();
                        return mode == "factory" ? InvalidInput() : Result<TaskParameters>.Success(values);
                    })
                    .Then(TaskOperations.Outbox, row =>
                    {
                        downstreamStarted = true;
                        return values with { Id = row.Id };
                    }).ExecuteAsync(cancellation.Token);
                if (result.IsSuccess || downstreamStarted || (await failed.CommitAsync()).IsSuccess) return Fail("pipeline-stop-" + mode);
                if (mode == "cancellation" && result.Errors[0].Category != ErrorCategory.Canceled) return Fail("pipeline-cancel-category");
            }
            if (!await CountAsync(provider, "SELECT COUNT(*) FROM f002_tasks WHERE id = 200", 0)) return Fail("pipeline-rollback-" + mode);
            Pass(provider, "pipeline-" + mode + "-failure-stops-and-rolls-back");
        }

        var session = (await repository.OpenSessionAsync()).ThrowIfError();
        await using (session)
        {
            var values = input with { Id = 300, Title = "nontransactional" };
            var result = await session.Pipeline(repository, TaskOperations.Write, values)
                .Then(new RepoCommandKey<TaskParameters, int> { Name = "missing" }, _ => values).ExecuteAsync();
            if (result.IsSuccess) return Fail("session-pipeline-failure");
        }
        if (!await CountAsync(provider, "SELECT COUNT(*) FROM f002_tasks WHERE id = 300", 1)) return Fail("session-pipeline-implied-atomicity");
        Pass(provider, "session-pipeline-does-not-imply-atomicity");
        return 0;
    }

    private static Result<TaskParameters> InvalidInput() => Result<TaskParameters>.Failure(Error.FromMessage("Invalid dependent input.", ErrorCategory.Validation));

    private static async Task<bool> CountAsync(IRepositoryProvider provider, string sql, long expected)
    {
        var connection = (await provider.OpenConnectionAsync()).ThrowIfError();
        await using (connection)
        {
            await using var query = connection.CreateCommand();
            query.CommandText = sql;
            return Convert.ToInt64(await query.ExecuteScalarAsync()) == expected;
        }
    }

    private static void Pass(IRepositoryProvider provider, string scenario) => Console.WriteLine($"PASS {provider.DatabaseProvider} {scenario}");
    private static int Fail(string scenario) { Console.Error.WriteLine("FAIL " + scenario); return 1; }
}
