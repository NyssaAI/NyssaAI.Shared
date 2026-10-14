using System.Data.Common;
using Microsoft.Data.Sqlite;
using NyssaAI.Repository.Providers;

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

    public async Task<MigrationReport> MigrateAsync(string release, CancellationToken cancellationToken = default)
    {
        if (_catalog is null) throw new InvalidOperationException("No migration catalog is registered for this provider.");
        var target = ((IMigrations)_catalog).Resolve(release);
        cancellationToken.ThrowIfCancellationRequested();
        var connection = await _provider.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
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
                    if (acquired is SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED)
                        throw new InvalidOperationException("The migration write lock could not be acquired.");
                    SqliteException.ThrowExceptionForRC(acquired, sqlite.Handle);
                    sqliteActive = true;
                }
                else if (_provider.DatabaseProvider == "postgresql" && connection is Npgsql.NpgsqlConnection)
                {
                    transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                    await using (var acquire = Command(connection, transaction, "SELECT pg_try_advisory_xact_lock(564535375002)"))
                    {
                        if (await acquire.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not true)
                            throw new InvalidOperationException("Another migration runner owns this database.");
                        await using (var quoting = Command(connection, transaction, "SHOW standard_conforming_strings"))
                        {
                            if (await quoting.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not string setting || setting != "on")
                                throw new InvalidOperationException("Migrations require PostgreSQL standard_conforming_strings=on.");
                        }
                    }
                }
                else throw new NotSupportedException("This provider does not support verified transactional migrations.");

                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteAsync(connection, transaction, "CREATE TABLE IF NOT EXISTS nyssa_repository_migrations (number BIGINT PRIMARY KEY, fingerprint TEXT NOT NULL)", cancellationToken).ConfigureAwait(false);
                var current = await ReadHistoryAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                // Preflight the entire downgrade before executing any schema change.
                if (current > target && _catalog.Definitions.Skip((int)target).Take((int)(current - target)).Any(step => step.DownSql is null))
                    throw new InvalidOperationException("The requested downgrade includes an irreversible migration.");

                var applied = 0;
                if (current < target)
                {
                    for (var number = current + 1; number <= target; number++)
                    {
                        var definition = _catalog.Definitions[(int)number - 1];
                        await ExecuteAsync(connection, transaction, definition.UpSql, cancellationToken).ConfigureAwait(false);
                        await using (var insert = Command(connection, transaction, "INSERT INTO nyssa_repository_migrations (number, fingerprint) VALUES (@number, @fingerprint)"))
                        {
                            AddParameter(insert, "number", number);
                            AddParameter(insert, "fingerprint", MigrationCatalog.Fingerprint(definition));
                            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                            applied++;
                        }
                    }
                }
                else
                {
                    for (var number = current; number > target; number--)
                    {
                        // The complete range was checked above; SQL is immutable in the catalog snapshot.
                        await ExecuteAsync(connection, transaction, _catalog.Definitions[(int)number - 1].DownSql!, cancellationToken).ConfigureAwait(false);
                        await using (var delete = Command(connection, transaction, "DELETE FROM nyssa_repository_migrations WHERE number = @number"))
                        {
                            AddParameter(delete, "number", number);
                            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                            applied++;
                        }
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (sqlite is not null)
                {
                    if (SQLitePCL.raw.sqlite3_get_autocommit(sqlite.Handle) != 0)
                        throw new InvalidOperationException("Migration SQL ended the runner's transaction.");
                    var committed = SQLitePCL.raw.sqlite3_exec(sqlite.Handle, "COMMIT");
                    SqliteException.ThrowExceptionForRC(committed, sqlite.Handle);
                    sqliteActive = false;
                }
                else await transaction!.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new() { FromNumber = current, ToNumber = target, AppliedCount = applied };
            }
            finally
            {
                if (sqliteActive && sqlite is not null) SQLitePCL.raw.sqlite3_exec(sqlite.Handle, "ROLLBACK");
                if (transaction is not null) await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<long> ReadHistoryAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        await using (var command = Command(connection, transaction, "SELECT number, fingerprint FROM nyssa_repository_migrations ORDER BY number"))
        {
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                long current = 0;
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    // SQLite's typed getters can coerce corrupt REAL values; require the stored contract before comparing history.
                    if (reader.GetValue(0) is not long number || reader.GetValue(1) is not string fingerprint ||
                        number != current + 1 || number > _catalog!.Definitions.Count ||
                        !string.Equals(fingerprint, MigrationCatalog.Fingerprint(_catalog.Definitions[(int)number - 1]), StringComparison.Ordinal))
                        throw new InvalidOperationException("Migration history does not match the compiled catalog prefix.");
                    current = number;
                }
                return current;
            }
        }
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
        await using (var command = Command(connection, transaction, sql))
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

}
