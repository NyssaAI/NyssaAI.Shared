using System.Data.Common;
using Microsoft.Data.Sqlite;
using NyssaAI.Repository.Providers;
using NyssaAI.Results;

namespace NyssaAI.Repository.Migrations;

internal sealed class MigrationRunner : IMigrationRunner
{
    private readonly IRepositoryProvider _provider;
    private readonly MigrationCatalog? _catalog;

    internal MigrationRunner(IRepositoryProvider provider, MigrationCatalog? catalog)
    {
        _provider = provider;
        _catalog = catalog;
    }

    public async Task<Result<MigrationReport>> MigrateAsync(string release, CancellationToken cancellationToken = default)
    {
        if (_catalog is null) return Failure("No migration catalog is registered for this provider.", ErrorCategory.NotFound);
        var targetResult = ((IMigrations)_catalog).Resolve(release);
        if (!targetResult.TryGetValue(out var target)) return Result<MigrationReport>.Failure(targetResult.Errors);
        if (cancellationToken.IsCancellationRequested) return Failure(Error.CanceledUserMessage, ErrorCategory.Canceled);
        var opened = await _provider.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (!opened.TryGetValue(out var connection)) return Result<MigrationReport>.Failure(opened.Errors);
        await using (connection.ConfigureAwait(false))
        {
            DbTransaction? transaction = null;
            var sqlite = connection as SqliteConnection;
            var sqliteActive = false;
            try
            {
                if (_provider.DatabaseProvider == "sqlite" && sqlite is not null)
                {
                    // Bypass SqliteCommand's busy retry only for lock acquisition. A competing process must fail immediately.
                    SQLitePCL.raw.sqlite3_busy_timeout(sqlite.Handle, 0);
                    var acquired = SQLitePCL.raw.sqlite3_exec(sqlite.Handle, "BEGIN IMMEDIATE");
                    if (acquired != SQLitePCL.raw.SQLITE_OK)
                        return Failure("The migration write lock could not be acquired.", ErrorCategory.Conflict);
                    sqliteActive = true;
                }
                else if (_provider.DatabaseProvider == "postgresql" && connection is Npgsql.NpgsqlConnection)
                {
                    transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                    await using var acquire = Command(connection, transaction, "SELECT pg_try_advisory_xact_lock(564535375002)");
                    if (await acquire.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not true)
                        return Failure("Another migration runner owns this database.", ErrorCategory.Conflict);
                    await using var quoting = Command(connection, transaction, "SHOW standard_conforming_strings");
                    if (await quoting.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not string setting || setting != "on")
                        return Failure("Migrations require PostgreSQL standard_conforming_strings=on.", ErrorCategory.Validation);
                }
                else return Failure("This provider does not support verified transactional migrations.", ErrorCategory.Validation);

                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteAsync(connection, transaction, "CREATE TABLE IF NOT EXISTS nyssa_repository_migrations (number BIGINT PRIMARY KEY, fingerprint TEXT NOT NULL)", cancellationToken).ConfigureAwait(false);
                var history = await ReadHistoryAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                if (!history.TryGetValue(out var current)) return Result<MigrationReport>.Failure(history.Errors);
                // Preflight the entire downgrade before executing any schema change.
                if (current > target && _catalog.Definitions.Skip((int)target).Take((int)(current - target)).Any(step => step.DownSql is null))
                    return Failure("The requested downgrade includes an irreversible migration.", ErrorCategory.Conflict);

                var applied = 0;
                if (current < target)
                {
                    for (var number = current + 1; number <= target; number++)
                    {
                        var definition = _catalog.Definitions[(int)number - 1];
                        await ExecuteAsync(connection, transaction, definition.UpSql, cancellationToken).ConfigureAwait(false);
                        await using var insert = Command(connection, transaction, "INSERT INTO nyssa_repository_migrations (number, fingerprint) VALUES (@number, @fingerprint)");
                        AddParameter(insert, "number", number);
                        AddParameter(insert, "fingerprint", MigrationCatalog.Fingerprint(definition));
                        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        applied++;
                    }
                }
                else
                {
                    for (var number = current; number > target; number--)
                    {
                        // The complete range was checked above; SQL is immutable in the catalog snapshot.
                        await ExecuteAsync(connection, transaction, _catalog.Definitions[(int)number - 1].DownSql!, cancellationToken).ConfigureAwait(false);
                        await using var delete = Command(connection, transaction, "DELETE FROM nyssa_repository_migrations WHERE number = @number");
                        AddParameter(delete, "number", number);
                        await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        applied++;
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (sqlite is not null)
                {
                    if (SQLitePCL.raw.sqlite3_get_autocommit(sqlite.Handle) != 0)
                        return Failure("Migration SQL ended the runner's transaction.", ErrorCategory.Conflict);
                    var committed = SQLitePCL.raw.sqlite3_exec(sqlite.Handle, "COMMIT");
                    if (committed != SQLitePCL.raw.SQLITE_OK) return Failure("The migration transaction could not be committed.", ErrorCategory.Unavailable);
                    sqliteActive = false;
                }
                else await transaction!.CommitAsync(cancellationToken).ConfigureAwait(false);
                return Result<MigrationReport>.Success(new() { FromNumber = current, ToNumber = target, AppliedCount = applied });
            }
            catch (OperationCanceledException) { return Failure(Error.CanceledUserMessage, ErrorCategory.Canceled); }
            catch (DbException) { return Failure("The migration operation failed.", ErrorCategory.Unavailable); }
            catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
            { return Failure("Migration history is invalid.", ErrorCategory.Conflict); }
            finally
            {
                if (sqliteActive && sqlite is not null) SQLitePCL.raw.sqlite3_exec(sqlite.Handle, "ROLLBACK");
                if (transaction is not null) await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<Result<long>> ReadHistoryAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, "SELECT number, fingerprint FROM nyssa_repository_migrations ORDER BY number");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        long current = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // SQLite's typed getters can coerce corrupt REAL values; require the stored contract before comparing history.
            if (reader.GetValue(0) is not long number || reader.GetValue(1) is not string fingerprint ||
                number != current + 1 || number > _catalog!.Definitions.Count ||
                !string.Equals(fingerprint, MigrationCatalog.Fingerprint(_catalog.Definitions[(int)number - 1]), StringComparison.Ordinal))
                return Result<long>.Failure(Error.FromMessage("Migration history does not match the compiled catalog prefix.", ErrorCategory.Conflict));
            current = number;
        }
        return Result<long>.Success(current);
    }

    private static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 30;
        return command;
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, sql);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static Result<MigrationReport> Failure(string message, ErrorCategory category) => Result<MigrationReport>.Failure(Error.FromMessage(message, category));
}
