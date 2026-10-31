using System.Data.Common;
using NyssaAI.DbGateway.Commands;

namespace NyssaAI.DbGateway.Gateway;

/// <summary>Executes typed commands and queries against an open <see cref="DbConnection"/> and optional <see cref="DbTransaction"/>.</summary>
internal static class DbCommandExecutor
{
    internal static async Task<int> ExecuteNonQueryAsync(
        DbConnection connection,
        DbTransaction? transaction,
        DbDialect dialect,
        int commandTimeoutSeconds,
        IDbNonQuery operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var sql = operation.GetSql(dialect);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        cancellationToken.ThrowIfCancellationRequested();

        await using var command = CreateCommand(connection, transaction, sql, commandTimeoutSeconds);
        var binder = new DbParameterBinder(command, dialect);
        operation.BindParameters(binder, dialect);
        cancellationToken.ThrowIfCancellationRequested();
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<TResult> ExecuteCommandAsync<TResult>(
        DbConnection connection,
        DbTransaction? transaction,
        DbDialect dialect,
        int commandTimeoutSeconds,
        IDbCommand<TResult> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var sql = operation.GetSql(dialect);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        cancellationToken.ThrowIfCancellationRequested();

        await using var command = CreateCommand(connection, transaction, sql, commandTimeoutSeconds);
        var binder = new DbParameterBinder(command, dialect);
        operation.BindParameters(binder, dialect);
        cancellationToken.ThrowIfCancellationRequested();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var recordReader = new DbRecordReader(reader);
        return await operation.MapResultAsync(recordReader, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<IReadOnlyList<TRow>> ExecuteQueryAsync<TRow>(
        DbConnection connection,
        DbTransaction? transaction,
        DbDialect dialect,
        int commandTimeoutSeconds,
        IDbQuery<TRow> query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var sql = query.GetSql(dialect);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        cancellationToken.ThrowIfCancellationRequested();

        await using var command = CreateCommand(connection, transaction, sql, commandTimeoutSeconds);
        var binder = new DbParameterBinder(command, dialect);
        query.BindParameters(binder, dialect);
        cancellationToken.ThrowIfCancellationRequested();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var recordReader = new DbRecordReader(reader);
        var results = new List<TRow>();
        while (await recordReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(query.MapRow(recordReader));
        }
        return results;
    }

    private static DbCommand CreateCommand(DbConnection connection, DbTransaction? transaction, string sql, int commandTimeoutSeconds)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;
        return command;
    }
}
