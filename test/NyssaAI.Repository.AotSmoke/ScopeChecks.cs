using System.Diagnostics;
using NyssaAI.Repository.AotCommands.Tasks;
using NyssaAI.Repository.Providers;
using NyssaAI.Results;

namespace NyssaAI.Repository.AotSmoke;

internal static class ScopeChecks
{
    internal static async Task<int> RunAsync(IRepository repository, IRepositoryProvider provider)
    {
        var opening = await provider.OpenConnectionAsync();
        if (!opening.TryGetValue(out var setup)) return Fail("setup-open");
        await using (setup)
        {
            await using var ddl = setup.CreateCommand();
            ddl.CommandText = "DROP TABLE IF EXISTS f002_tasks; CREATE TABLE f002_tasks (id BIGINT PRIMARY KEY, title TEXT NOT NULL, note TEXT NULL)";
            await ddl.ExecuteNonQueryAsync();
        }
        var input = new TaskParameters { Id = 1, Title = "quoted ' ; DROP TABLE f002_tasks; -- Unicode \u03A9\u4E2D\U0001F600", Note = null };
        var second = input with { Id = 2, Title = "second", Note = "not null" };
        var missing = input with { Id = 999, Title = "missing" };
        var sessionResult = await repository.OpenSessionAsync();
        if (!sessionResult.TryGetValue(out var session)) return Fail("session-open");
        IReadOnlyList<TaskRow> retained;
        await using (session)
        {
            var countCommand = repository.CommandRegistry(TaskOperations.Count, input);
            if (!countCommand.TryGetValue(out var countQuery)) return Fail("count-resolve");
            var emptyCount = await session.ExecuteAsync(countQuery);
            if (!emptyCount.TryGetValue(out var zero) || zero != 0) return Fail("count-empty");
            var listCommand = repository.CommandRegistry(TaskOperations.List, input);
            if (!listCommand.TryGetValue(out var listQuery)) return Fail("list-resolve");
            var emptyList = await session.ExecuteAsync(listQuery);
            if (!emptyList.TryGetValue(out var empty) || empty.Count != 0) return Fail("list-empty");
            Pass(provider, "empty-scalar-and-buffered-list");

            var resolved = repository.CommandRegistry(TaskOperations.Write, input);
            if (!resolved.TryGetValue(out var command)) return Fail("write-resolve");
            var written = await session.ExecuteAsync(command);
            if (!written.TryGetValue(out var count) || count != 1) return Fail("write-count");
            var find = repository.CommandRegistry(TaskOperations.Find, input);
            if (!find.TryGetValue(out var query)) return Fail("find-resolve");
            var found = await session.ExecuteAsync(query);
            if (!found.TryGetValue(out var row) || row.Id != input.Id || row.Title != input.Title || row.Note is not null) return Fail("required-init-row");
            Pass(provider, "write-bound-sql-like-unicode-required-init-nullable-row");

            var another = repository.CommandRegistry(TaskOperations.Write, second);
            if (!another.TryGetValue(out var anotherWrite)) return Fail("second-write-resolve");
            var anotherWritten = await session.ExecuteAsync(anotherWrite);
            if (!anotherWritten.TryGetValue(out var anotherCount) || anotherCount != 1) return Fail("second-write");
            var populatedCount = await session.ExecuteAsync(countQuery);
            if (!populatedCount.TryGetValue(out var two) || two != 2) return Fail("count-populated");
            var listed = await session.ExecuteAsync(listQuery);
            if (!listed.TryGetValue(out var rows) || rows.Count != 2 || rows[1].Note != second.Note) return Fail("list-populated");
            retained = rows;
            var delete = repository.CommandRegistry(TaskOperations.Delete, missing);
            if (!delete.TryGetValue(out var deleteMissing)) return Fail("delete-resolve");
            var deleted = await session.ExecuteAsync(deleteMissing);
            if (!deleted.TryGetValue(out var affected) || affected != 0) return Fail("zero-affected-rows");
            Pass(provider, "populated-scalar-list-zero-affected-rows");
        }
        if (retained.Count != 2 || retained[0].Title != input.Title) return Fail("buffered-after-disposal");
        Pass(provider, "buffered-after-scope-disposal");

        var absentScope = await repository.OpenSessionAsync();
        if (!absentScope.TryGetValue(out var absentSession)) return Fail("absent-open");
        await using (absentSession)
        {
            var resolved = repository.CommandRegistry(TaskOperations.Find, missing);
            if (!resolved.TryGetValue(out var command)) return Fail("absent-resolve");
            var result = await absentSession.ExecuteAsync(command);
            if (result.IsSuccess || result.Errors[0].Category != ErrorCategory.NotFound) return Fail("exactly-one-zero");
        }
        var multipleScope = await repository.OpenTransactionAsync();
        if (!multipleScope.TryGetValue(out var multiple)) return Fail("multiple-open");
        await using (multiple)
        {
            var duplicate = repository.CommandRegistry(TaskOperations.Write, input with { Id = 3 });
            if (!duplicate.TryGetValue(out var write)) return Fail("multiple-write-resolve");
            var written = await multiple.ExecuteAsync(write);
            if (!written.TryGetValue(out var count) || count != 1) return Fail("multiple-write");
            var resolved = repository.CommandRegistry(TaskOperations.Find, input);
            if (!resolved.TryGetValue(out var command)) return Fail("multiple-find-resolve");
            var result = await multiple.ExecuteAsync(command);
            if (result.IsSuccess || result.Errors[0].Category != ErrorCategory.Validation) return Fail("exactly-one-many");
            if ((await multiple.CommitAsync()).IsSuccess) return Fail("cardinality-failure-commit");
        }
        Pass(provider, "exactly-one-zero-and-many-result-categories");

        var transactionResult = await repository.OpenTransactionAsync();
        if (!transactionResult.TryGetValue(out var transaction)) return Fail("transaction-open");
        await using (transaction)
        {
            var uncommitted = repository.CommandRegistry(TaskOperations.Write, input with { Id = 10, Title = "rollback" });
            if (!uncommitted.TryGetValue(out var write)) return Fail("transaction-resolve");
            var written = await transaction.ExecuteAsync(write);
            if (!written.TryGetValue(out var count) || count != 1) return Fail("transaction-write");
            var reportCommand = repository.CommandRegistry(TaskOperations.Report, input);
            if (!reportCommand.TryGetValue(out var reportQuery)) return Fail("report-resolve");
            var reportResult = await transaction.ExecuteAsync(reportQuery);
            if (!reportResult.TryGetValue(out var report) || report.Count != 3 || report.Rows.Count != 3 || report.Rows[2].Id != 10) return Fail("composite-enlistment");
            if ((await transaction.RollbackAsync()).IsFailure) return Fail("rollback");
        }
        if (!await ObserveCountAsync(provider, "SELECT COUNT(*) FROM f002_tasks WHERE id IN (3, 10)", 0)) return Fail("rollback-independent-observer");
        Pass(provider, "composite-both-calls-enlisted-rollback-independent-observer");

        var failureScope = await repository.OpenTransactionAsync();
        if (!failureScope.TryGetValue(out var failureTransaction)) return Fail("failure-open");
        await using (failureTransaction)
        {
            var report = repository.CommandRegistry(TaskOperations.ReportFailure, input);
            if (!report.TryGetValue(out var reportQuery)) return Fail("failure-report-resolve");
            var result = await failureTransaction.ExecuteAsync(reportQuery);
            if (result.IsSuccess) return Fail("partial-composite-success");
            if ((await failureTransaction.CommitAsync()).IsSuccess) return Fail("composite-failure-commit");
        }
        Pass(provider, "composite-later-query-failure-no-partial-success");

        var canceledScope = await repository.OpenTransactionAsync();
        if (!canceledScope.TryGetValue(out var canceledTransaction)) return Fail("precancel-open");
        await using (canceledTransaction)
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var resolved = repository.CommandRegistry(TaskOperations.Write, input with { Id = 20 });
            if (!resolved.TryGetValue(out var command)) return Fail("precancel-resolve");
            var result = await canceledTransaction.ExecuteAsync(command, cancellation.Token);
            if (result.IsSuccess || result.Errors[0].Category != ErrorCategory.Canceled) return Fail("precancel-generated-driver");
            if ((await canceledTransaction.ExecuteAsync(command)).IsSuccess) return Fail("precancel-later-execution");
            if ((await canceledTransaction.CommitAsync()).IsSuccess) return Fail("precancel-commit");
        }
        if (!await ObserveCountAsync(provider, "SELECT COUNT(*) FROM f002_tasks WHERE id = 20", 0)) return Fail("precancel-observer");
        Pass(provider, "precanceled-scope-failed-scope-cleanup");

        if (provider.DatabaseProvider == "postgresql")
        {
            var inflight = await CheckInflightAsync(repository, provider, input);
            if (inflight != 0) return inflight;
        }
        else
        {
            Console.WriteLine("LIMIT sqlite: synchronous execution; no claim of running-query interruption; Cancel is a no-op.");
        }
        if (!await ObserveCountAsync(provider, "SELECT COUNT(*) FROM f002_tasks", 2)) return Fail("final-persistence");
        Pass(provider, "final-persistence-and-provider-reuse");
        if (await CheckLifecycleAsync(repository, provider, input) != 0) return 1;
        return 0;
    }

    private static async Task<int> CheckLifecycleAsync(IRepository repository, IRepositoryProvider provider, TaskParameters input)
    {
        var committed = (await repository.OpenTransactionAsync()).ThrowIfError();
        await using (committed)
        {
            var write = repository.CommandRegistry(TaskOperations.Write, input with { Id = 40, Title = "committed" }).ThrowIfError();
            if ((await committed.ExecuteAsync(write)).IsFailure || (await committed.CommitAsync()).IsFailure) return Fail("explicit-commit");
            if ((await committed.ExecuteAsync(write)).IsSuccess || (await committed.CommitAsync()).IsSuccess || (await committed.RollbackAsync()).IsSuccess) return Fail("completed-transaction-accepted-work");
        }
        if (!await ObserveCountAsync(provider, "SELECT COUNT(*) FROM f002_tasks WHERE id = 40", 1)) return Fail("commit-observer");
        var command = repository.CommandRegistry(TaskOperations.Count, input).ThrowIfError();
        if ((await committed.ExecuteAsync(command)).IsSuccess) return Fail("disposed-transaction");
        var rolledBack = (await repository.OpenTransactionAsync()).ThrowIfError();
        await using (rolledBack)
        {
            if ((await rolledBack.RollbackAsync()).IsFailure || (await rolledBack.ExecuteAsync(command)).IsSuccess) return Fail("rolledback-execution");
        }
        var disposedSession = (await repository.OpenSessionAsync()).ThrowIfError();
        await disposedSession.DisposeAsync();
        if ((await disposedSession.ExecuteAsync(command)).IsSuccess) return Fail("disposed-session");
        Pass(provider, "commit-rollback-dispose-lifecycle-independent-persistence");

        var malformedTransaction = (await repository.OpenTransactionAsync()).ThrowIfError();
        await using (malformedTransaction)
        {
            var malformed = repository.CommandRegistry(TaskOperations.Malformed, input).ThrowIfError();
            var result = await malformedTransaction.ExecuteAsync(malformed);
            if (result.IsSuccess || result.Errors.Any(error => error.UserMessage.Contains("invalid-integer", StringComparison.Ordinal))) return Fail("materialization-sanitization");
            if ((await malformedTransaction.CommitAsync()).IsSuccess) return Fail("materialization-commit");
        }
        Pass(provider, "materialization-failure-sanitized-poisons-transaction");

        var first = (await repository.OpenSessionAsync()).ThrowIfError();
        var second = (await repository.OpenSessionAsync()).ThrowIfError();
        await using (first)
        await using (second)
        {
            var results = await Task.WhenAll(first.ExecuteAsync(command), second.ExecuteAsync(command));
            if (results.Any(result => !result.TryGetValue(out var count) || count != 3)) return Fail("independent-concurrent-sessions");
        }
        Pass(provider, "independent-concurrent-sessions");
        if (provider.DatabaseProvider == "sqlite")
        {
            var locked = (await repository.OpenTransactionAsync()).ThrowIfError();
            await using (locked)
            {
                var write = repository.CommandRegistry(TaskOperations.Write, input with { Id = 41 }).ThrowIfError();
                if ((await locked.ExecuteAsync(write)).IsFailure) return Fail("lock-holder-write");
                var blocked = (await repository.OpenSessionAsync()).ThrowIfError();
                await using (blocked)
                {
                    var blockedWrite = repository.CommandRegistry(TaskOperations.Write, input with { Id = 42 }).ThrowIfError();
                    if ((await blocked.ExecuteAsync(blockedWrite)).IsSuccess) return Fail("lock-timeout-success");
                }
            }
            if (!await ObserveCountAsync(provider, "SELECT COUNT(*) FROM f002_tasks WHERE id IN (41, 42)", 0)) return Fail("lock-timeout-cleanup");
            Pass(provider, "sqlite-lock-timeout-cleanup");
        }
        return 0;
    }

    private static async Task<int> CheckInflightAsync(IRepository repository, IRepositoryProvider provider, TaskParameters input)
    {
        var observerResult = await provider.OpenConnectionAsync();
        if (!observerResult.TryGetValue(out var observer)) return Fail("inflight-observer-open");
        await using (observer)
        {
            var scopeResult = await repository.OpenTransactionAsync();
            if (!scopeResult.TryGetValue(out var transaction)) return Fail("inflight-scope-open");
            await using (transaction)
            using (var cancellation = new CancellationTokenSource())
            {
                var writeResult = repository.CommandRegistry(TaskOperations.Write, input with { Id = 30, Title = "cancel-rollback" });
                if (!writeResult.TryGetValue(out var write)) return Fail("inflight-write-resolve");
                if ((await transaction.ExecuteAsync(write)).IsFailure) return Fail("inflight-write");
                var delayResult = repository.CommandRegistry(TaskOperations.Delay, input with { Id = 30 });
                if (!delayResult.TryGetValue(out var delay)) return Fail("delay-resolve");
                var pending = transaction.ExecuteAsync(delay, cancellation.Token);
                await using var query = observer.CreateCommand();
                query.CommandText = "SELECT COUNT(*) FROM pg_stat_activity WHERE state = 'active' AND query LIKE '/*f002-inflight*/%'";
                var deadline = Stopwatch.StartNew();
                var observed = false;
                while (deadline.Elapsed < TimeSpan.FromSeconds(10) && !pending.IsCompleted)
                {
                    if (Convert.ToInt64(await query.ExecuteScalarAsync()) > 0) { observed = true; break; }
                    await Task.Delay(20);
                }
                cancellation.Cancel();
                var canceled = await pending.WaitAsync(TimeSpan.FromSeconds(10));
                if (!observed) return Fail("inflight-not-observed-active");
                if (canceled.IsSuccess || canceled.Errors[0].Category != ErrorCategory.Canceled) return Fail("inflight-canceled-result");
                if ((await transaction.ExecuteAsync(write)).IsSuccess) return Fail("inflight-later-execution");
                if ((await transaction.CommitAsync()).IsSuccess) return Fail("inflight-commit");
                if ((await transaction.RollbackAsync()).IsFailure) return Fail("inflight-rollback");
            }
        }
        if (!await ObserveCountAsync(provider, "SELECT COUNT(*) FROM f002_tasks WHERE id = 30", 0)) return Fail("inflight-independent-observer");
        var newScope = await repository.OpenSessionAsync();
        if (!newScope.TryGetValue(out var session)) return Fail("inflight-reuse-open");
        await using (session)
        {
            var count = repository.CommandRegistry(TaskOperations.Count, input);
            if (!count.TryGetValue(out var query)) return Fail("inflight-reuse-resolve");
            var result = await session.ExecuteAsync(query);
            if (!result.TryGetValue(out var value) || value != 2) return Fail("inflight-reuse-execute");
        }
        Pass(provider, "observer-confirmed-inflight-cancel-refused-commit-rollback-new-scope");
        return 0;
    }

    private static async Task<bool> ObserveCountAsync(IRepositoryProvider provider, string sql, long expected)
    {
        var result = await provider.OpenConnectionAsync();
        if (!result.TryGetValue(out var connection)) return false;
        await using (connection)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync()) == expected;
        }
    }
    private static void Pass(IRepositoryProvider provider, string scenario) => Console.WriteLine($"PASS {provider.DatabaseProvider} {scenario}");
    private static int Fail(string scenario)
    {
        Console.Error.WriteLine($"FAIL {scenario}");
        return 1;
    }
}
