using System.Diagnostics;
using NyssaAI.Repository.AotCommands.Tasks;
using NyssaAI.Repository.Providers;

namespace NyssaAI.Repository.AotSmoke;

internal static class ScopeChecks
{
    internal static async Task<int> RunAsync(IRepository repository, IRepositoryProvider provider)
    {
        var opening = await provider.OpenConnectionAsync();
        var setup = opening;
        await using (setup)
        {
            await using (var ddl = setup.CreateCommand())
            {
                ddl.CommandText = "DROP TABLE IF EXISTS f002_tasks; CREATE TABLE f002_tasks (id BIGINT PRIMARY KEY, title TEXT NOT NULL, note TEXT NULL)";
                await ddl.ExecuteNonQueryAsync();
            }
        }
        var input = new TaskParameters { Id = 1, Title = "quoted ' ; DROP TABLE f002_tasks; -- Unicode \u03A9\u4E2D\U0001F600", Note = null };
        var second = input with { Id = 2, Title = "second", Note = "not null" };
        var missing = input with { Id = 999, Title = "missing" };
        var sessionResult = await repository.OpenSessionAsync();
        var session = sessionResult;
        IReadOnlyList<TaskRow> retained;
        await using (session)
        {
            var countCommand = repository.CommandRegistry(TaskOperations.Count, input);
            var countQuery = countCommand;
            var emptyCount = await session.ExecuteAsync(countQuery);
            var zero = emptyCount;
            if (zero != 0) return Fail("count-empty");
            var listCommand = repository.CommandRegistry(TaskOperations.List, input);
            var listQuery = listCommand;
            var emptyList = await session.ExecuteAsync(listQuery);
            var empty = emptyList;
            if (empty.Count != 0) return Fail("list-empty");
            Pass(provider, "empty-scalar-and-buffered-list");

            var resolved = repository.CommandRegistry(TaskOperations.Write, input);
            var command = resolved;
            var written = await session.ExecuteAsync(command);
            var count = written;
            if (count != 1) return Fail("write-count");
            var find = repository.CommandRegistry(TaskOperations.Find, input);
            var query = find;
            var found = await session.ExecuteAsync(query);
            var row = found;
            if (row.Id != input.Id || row.Title != input.Title || row.Note is not null) return Fail("required-init-row");
            Pass(provider, "write-bound-sql-like-unicode-required-init-nullable-row");

            var another = repository.CommandRegistry(TaskOperations.Write, second);
            var anotherWrite = another;
            var anotherWritten = await session.ExecuteAsync(anotherWrite);
            var anotherCount = anotherWritten;
            if (anotherCount != 1) return Fail("second-write");
            var populatedCount = await session.ExecuteAsync(countQuery);
            var two = populatedCount;
            if (two != 2) return Fail("count-populated");
            var listed = await session.ExecuteAsync(listQuery);
            var rows = listed;
            if (rows.Count != 2 || rows[1].Note != second.Note) return Fail("list-populated");
            retained = rows;
            var delete = repository.CommandRegistry(TaskOperations.Delete, missing);
            var deleteMissing = delete;
            var deleted = await session.ExecuteAsync(deleteMissing);
            var affected = deleted;
            if (affected != 0) return Fail("zero-affected-rows");
            Pass(provider, "populated-scalar-list-zero-affected-rows");
        }
        if (retained.Count != 2 || retained[0].Title != input.Title) return Fail("buffered-after-disposal");
        Pass(provider, "buffered-after-scope-disposal");

        var absentScope = await repository.OpenSessionAsync();
        var absentSession = absentScope;
        await using (absentSession)
        {
            var resolved = repository.CommandRegistry(TaskOperations.Find, missing);
            var command = resolved;
            var result = await SmokeCheck.CaptureAsync(() => absentSession.ExecuteAsync(command));
            if (result is not KeyNotFoundException) return Fail("exactly-one-zero");
        }
        var multipleScope = await repository.OpenTransactionAsync();
        var multiple = multipleScope;
        await using (multiple)
        {
            var duplicate = repository.CommandRegistry(TaskOperations.Write, input with { Id = 3 });
            var write = duplicate;
            var written = await multiple.ExecuteAsync(write);
            var count = written;
            if (count != 1) return Fail("multiple-write");
            var resolved = repository.CommandRegistry(TaskOperations.Find, input);
            var command = resolved;
            var result = await SmokeCheck.CaptureAsync(() => multiple.ExecuteAsync(command));
            if (result is not InvalidOperationException) return Fail("exactly-one-many");
            if (!await SmokeCheck.ThrowsAsync(() => multiple.CommitAsync())) return Fail("cardinality-failure-commit");
        }
        Pass(provider, "exactly-one-zero-and-many-exceptions");

        var transactionResult = await repository.OpenTransactionAsync();
        var transaction = transactionResult;
        await using (transaction)
        {
            var uncommitted = repository.CommandRegistry(TaskOperations.Write, input with { Id = 10, Title = "rollback" });
            var write = uncommitted;
            var written = await transaction.ExecuteAsync(write);
            var count = written;
            if (count != 1) return Fail("transaction-write");
            var reportCommand = repository.CommandRegistry(TaskOperations.Report, input);
            var reportQuery = reportCommand;
            var reportResult = await transaction.ExecuteAsync(reportQuery);
            var report = reportResult;
            if (report.Count != 3 || report.Rows.Count != 3 || report.Rows[2].Id != 10) return Fail("composite-enlistment");
            if (!await SmokeCheck.CompletesAsync(() => transaction.RollbackAsync())) return Fail("rollback");
        }
        if (!await ObserveCountAsync(provider, "SELECT COUNT(*) FROM f002_tasks WHERE id IN (3, 10)", 0)) return Fail("rollback-independent-observer");
        Pass(provider, "composite-both-calls-enlisted-rollback-independent-observer");

        var failureScope = await repository.OpenTransactionAsync();
        var failureTransaction = failureScope;
        await using (failureTransaction)
        {
            var report = repository.CommandRegistry(TaskOperations.ReportFailure, input);
            var reportQuery = report;
            var result = await SmokeCheck.CaptureAsync(() => failureTransaction.ExecuteAsync(reportQuery));
            if (provider.DatabaseProvider == "sqlite" && result is not Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 1 }) return Fail("command-sqlite-diagnostics");
            if (provider.DatabaseProvider == "postgresql" && result is not Npgsql.PostgresException { SqlState: "42P01" }) return Fail("command-postgres-diagnostics");
            if (!await SmokeCheck.ThrowsAsync(() => failureTransaction.CommitAsync())) return Fail("composite-failure-commit");
        }
        Pass(provider, "composite-later-query-failure-no-partial-success");

        var canceledScope = await repository.OpenTransactionAsync();
        var canceledTransaction = canceledScope;
        await using (canceledTransaction)
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var resolved = repository.CommandRegistry(TaskOperations.Write, input with { Id = 20 });
            var command = resolved;
            var result = await SmokeCheck.CaptureAsync(() => canceledTransaction.ExecuteAsync(command, cancellation.Token));
            if (result is not OperationCanceledException) return Fail("precancel-generated-driver");
            if (!await SmokeCheck.ThrowsAsync(() => canceledTransaction.ExecuteAsync(command))) return Fail("precancel-later-execution");
            if (!await SmokeCheck.ThrowsAsync(() => canceledTransaction.CommitAsync())) return Fail("precancel-commit");
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
        var committed = (await repository.OpenTransactionAsync());
        await using (committed)
        {
            var write = repository.CommandRegistry(TaskOperations.Write, input with { Id = 40, Title = "committed" });
            if (!await SmokeCheck.CompletesAsync(() => committed.ExecuteAsync(write)) || !await SmokeCheck.CompletesAsync(() => committed.CommitAsync())) return Fail("explicit-commit");
            if (!await SmokeCheck.ThrowsAsync(() => committed.ExecuteAsync(write)) || !await SmokeCheck.ThrowsAsync(() => committed.CommitAsync()) || !await SmokeCheck.ThrowsAsync(() => committed.RollbackAsync())) return Fail("completed-transaction-accepted-work");
        }
        if (!await ObserveCountAsync(provider, "SELECT COUNT(*) FROM f002_tasks WHERE id = 40", 1)) return Fail("commit-observer");
        var command = repository.CommandRegistry(TaskOperations.Count, input);
        if (!await SmokeCheck.ThrowsAsync(() => committed.ExecuteAsync(command))) return Fail("disposed-transaction");
        var rolledBack = (await repository.OpenTransactionAsync());
        await using (rolledBack)
        {
            if (!await SmokeCheck.CompletesAsync(() => rolledBack.RollbackAsync()) || !await SmokeCheck.ThrowsAsync(() => rolledBack.ExecuteAsync(command))) return Fail("rolledback-execution");
        }
        var disposedSession = (await repository.OpenSessionAsync());
        await disposedSession.DisposeAsync();
        if (!await SmokeCheck.ThrowsAsync(() => disposedSession.ExecuteAsync(command))) return Fail("disposed-session");
        Pass(provider, "commit-rollback-dispose-lifecycle-independent-persistence");

        var malformedTransaction = (await repository.OpenTransactionAsync());
        await using (malformedTransaction)
        {
            var malformed = repository.CommandRegistry(TaskOperations.Malformed, input);
            var result = await SmokeCheck.CaptureAsync(() => malformedTransaction.ExecuteAsync(malformed));
            if (result is not (FormatException or InvalidCastException or OverflowException)) return Fail("materialization-exception");
            if (!await SmokeCheck.ThrowsAsync(() => malformedTransaction.CommitAsync())) return Fail("materialization-commit");
        }
        Pass(provider, "materialization-failure-preserved-prevents-commit");

        var first = (await repository.OpenSessionAsync());
        var second = (await repository.OpenSessionAsync());
        await using (first)
        await using (second)
        {
            var results = await Task.WhenAll(first.ExecuteAsync(command), second.ExecuteAsync(command));
            if (results.Any(count => count != 3)) return Fail("independent-concurrent-sessions");
        }
        Pass(provider, "independent-concurrent-sessions");
        if (provider.DatabaseProvider == "sqlite")
        {
            var locked = (await repository.OpenTransactionAsync());
            await using (locked)
            {
                var write = repository.CommandRegistry(TaskOperations.Write, input with { Id = 41 });
                if (!await SmokeCheck.CompletesAsync(() => locked.ExecuteAsync(write))) return Fail("lock-holder-write");
                var blocked = (await repository.OpenSessionAsync());
                await using (blocked)
                {
                    var blockedWrite = repository.CommandRegistry(TaskOperations.Write, input with { Id = 42 });
                    if (!await SmokeCheck.ThrowsAsync(() => blocked.ExecuteAsync(blockedWrite))) return Fail("lock-timeout-success");
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
        var observer = observerResult;
        await using (observer)
        {
            var scopeResult = await repository.OpenTransactionAsync();
            var transaction = scopeResult;
            await using (transaction)
            using (var cancellation = new CancellationTokenSource())
            {
                var writeResult = repository.CommandRegistry(TaskOperations.Write, input with { Id = 30, Title = "cancel-rollback" });
                var write = writeResult;
                if (!await SmokeCheck.CompletesAsync(() => transaction.ExecuteAsync(write))) return Fail("inflight-write");
                var delayResult = repository.CommandRegistry(TaskOperations.Delay, input with { Id = 30 });
                var delay = delayResult;
                var pending = transaction.ExecuteAsync(delay, cancellation.Token);
                await using (var query = observer.CreateCommand())
                {
                    query.CommandText = "SELECT COUNT(*) FROM pg_stat_activity WHERE state = 'active' AND query LIKE '/*f002-inflight*/%'";
                    var deadline = Stopwatch.StartNew();
                    var observed = false;
                    while (deadline.Elapsed < TimeSpan.FromSeconds(10) && !pending.IsCompleted)
                    {
                        if (Convert.ToInt64(await query.ExecuteScalarAsync()) > 0) { observed = true; break; }
                        await Task.Delay(20);
                    }
                    cancellation.Cancel();
                    var canceled = await SmokeCheck.CaptureAsync(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
                    if (!observed) return Fail("inflight-not-observed-active");
                    if (canceled is not OperationCanceledException) return Fail("inflight-canceled-result");
                    if (!await SmokeCheck.ThrowsAsync(() => transaction.ExecuteAsync(write))) return Fail("inflight-later-execution");
                    if (!await SmokeCheck.ThrowsAsync(() => transaction.CommitAsync())) return Fail("inflight-commit");
                    if (!await SmokeCheck.CompletesAsync(() => transaction.RollbackAsync())) return Fail("inflight-rollback");
                }
            }
        }
        if (!await ObserveCountAsync(provider, "SELECT COUNT(*) FROM f002_tasks WHERE id = 30", 0)) return Fail("inflight-independent-observer");
        var newScope = await repository.OpenSessionAsync();
        var session = newScope;
        await using (session)
        {
            var count = repository.CommandRegistry(TaskOperations.Count, input);
            var query = count;
            var result = await session.ExecuteAsync(query);
            var value = result;
            if (value != 2) return Fail("inflight-reuse-execute");
        }
        Pass(provider, "observer-confirmed-inflight-cancel-refused-commit-rollback-new-scope");
        return 0;
    }

    private static async Task<bool> ObserveCountAsync(IRepositoryProvider provider, string sql, long expected)
    {
        var result = await provider.OpenConnectionAsync();
        var connection = result;
        await using (connection)
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                return Convert.ToInt64(await command.ExecuteScalarAsync()) == expected;
            }
        }
    }
    private static void Pass(IRepositoryProvider provider, string scenario) => Console.WriteLine($"PASS {provider.DatabaseProvider} {scenario}");
    private static int Fail(string scenario)
    {
        Console.Error.WriteLine($"FAIL {scenario}");
        return 1;
    }
}
