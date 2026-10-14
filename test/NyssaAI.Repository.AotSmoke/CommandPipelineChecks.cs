using NyssaAI.Repository.AotCommands.Tasks;
using NyssaAI.Repository.CommandPipelines;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Providers;

namespace NyssaAI.Repository.AotSmoke;

internal static class CommandPipelineChecks
{
    internal static async Task<int> RunAsync(IRepository repository, IRepositoryProvider provider)
    {
        var setup = (await provider.OpenConnectionAsync());
        await using (setup)
        {
            await using (var ddl = setup.CreateCommand())
            {
                ddl.CommandText = "DROP TABLE IF EXISTS f002_outbox; CREATE TABLE f002_outbox (task_id BIGINT PRIMARY KEY, title TEXT NOT NULL)";
                await ddl.ExecuteNonQueryAsync();
            }
        }
        var input = new TaskParameters { Id = 100, Title = "fluent-outbox", Note = null };
        var transaction = (await repository.OpenTransactionAsync());
        await using (transaction)
        {
            var result = await transaction.Pipeline(repository, TaskOperations.Write, input)
                .Then(TaskOperations.Find, affected => affected == 1 ? input : InvalidInput())
                .Then(TaskOperations.Outbox, row => input with { Id = row.Id, Title = row.Title }).ExecuteAsync();
            var affected = result;
            if (affected != 1 || !await SmokeCheck.CompletesAsync(() => transaction.CommitAsync())) return Fail("pipeline-outbox");
        }
        var imperative = (await repository.OpenTransactionAsync());
        await using (imperative)
        {
            var values = input with { Id = 101, Title = "imperative-outbox" };
            var written = await imperative.ExecuteAsync(repository.CommandRegistry(TaskOperations.Write, values));
            var affected = written;
            if (affected != 1) return Fail("imperative-write");
            var found = await imperative.ExecuteAsync(repository.CommandRegistry(TaskOperations.Find, values));
            var row = found;
            var notified = await imperative.ExecuteAsync(repository.CommandRegistry(TaskOperations.Outbox, values with { Id = row.Id, Title = row.Title }));
            var count = notified;
            if (count != 1 || !await SmokeCheck.CompletesAsync(() => imperative.CommitAsync())) return Fail("imperative-outbox");
        }
        if (!await CountAsync(provider, "SELECT COUNT(*) FROM f002_tasks t INNER JOIN f002_outbox o ON t.id = o.task_id AND t.title = o.title", 2)) return Fail("outbox-persistence");
        Pass(provider, "pipeline-typed-dependencies-imperative-parity-atomic-outbox");

        foreach (var mode in new[] { "resolution", "factory", "cancellation" })
        {
            var failed = (await repository.OpenTransactionAsync());
            await using (failed)
            using (var cancellation = new CancellationTokenSource())
            {
                var values = input with { Id = 200, Title = mode };
                var missing = new RepoCommandKey<TaskParameters, TaskRow> { Name = "missing-after-write" };
                var next = mode == "resolution" ? missing : TaskOperations.Find;
                var downstreamStarted = false;
                var result = await SmokeCheck.CaptureAsync(() => failed.Pipeline(repository, TaskOperations.Write, values)
                    .Then(next, _ =>
                    {
                        if (mode == "cancellation") cancellation.Cancel();
                        return mode == "factory" ? InvalidInput() : values;
                    })
                    .Then(TaskOperations.Outbox, row =>
                    {
                        downstreamStarted = true;
                        return values with { Id = row.Id };
                    }).ExecuteAsync(cancellation.Token));
                if (downstreamStarted || !await SmokeCheck.ThrowsAsync(() => failed.CommitAsync())) return Fail("pipeline-stop-" + mode);
                if (mode == "cancellation" && result is not OperationCanceledException) return Fail("pipeline-cancel-exception");
            }
            if (!await CountAsync(provider, "SELECT COUNT(*) FROM f002_tasks WHERE id = 200", 0)) return Fail("pipeline-rollback-" + mode);
            Pass(provider, "pipeline-" + mode + "-failure-stops-and-rolls-back");
        }

        var session = (await repository.OpenSessionAsync());
        await using (session)
        {
            var values = input with { Id = 300, Title = "nontransactional" };
            var result = await SmokeCheck.CaptureAsync(() => session.Pipeline(repository, TaskOperations.Write, values)
                .Then(new RepoCommandKey<TaskParameters, int> { Name = "missing" }, _ => values).ExecuteAsync());
        }
        if (!await CountAsync(provider, "SELECT COUNT(*) FROM f002_tasks WHERE id = 300", 1)) return Fail("session-pipeline-implied-atomicity");
        Pass(provider, "session-pipeline-does-not-imply-atomicity");
        return 0;
    }

    private static TaskParameters InvalidInput() => throw new ArgumentException("Invalid dependent input.");

    private static async Task<bool> CountAsync(IRepositoryProvider provider, string sql, long expected)
    {
        var connection = (await provider.OpenConnectionAsync());
        await using (connection)
        {
            await using (var query = connection.CreateCommand())
            {
                query.CommandText = sql;
                return Convert.ToInt64(await query.ExecuteScalarAsync()) == expected;
            }
        }
    }

    private static void Pass(IRepositoryProvider provider, string scenario) => Console.WriteLine($"PASS {provider.DatabaseProvider} {scenario}");
    private static int Fail(string scenario) { Console.Error.WriteLine("FAIL " + scenario); return 1; }
}
