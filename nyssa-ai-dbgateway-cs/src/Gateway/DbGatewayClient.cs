using System.Data.Common;
using NyssaAI.DbGateway.Commands;
using NyssaAI.DbGateway.Providers;
using NyssaAI.DbGateway.Transactions;

namespace NyssaAI.DbGateway.Gateway;

/// <summary>Implements <see cref="IDbGateway"/> over an <see cref="IDbProvider"/>.</summary>
internal sealed class DbGatewayClient : IDbGateway
{
    private readonly IDbProvider _provider;
    private readonly int _commandTimeoutSeconds;

    internal DbGatewayClient(IDbProvider provider, int commandTimeoutSeconds = 30)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentOutOfRangeException.ThrowIfNegative(commandTimeoutSeconds);
        _provider = provider;
        _commandTimeoutSeconds = commandTimeoutSeconds;
    }

    public DbDialect Dialect => _provider.Dialect;

    public string DatabaseProvider => _provider.DatabaseProvider;

    public async Task<int> ExecuteAsync(IDbNonQuery command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var connection = await _provider.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await DbCommandExecutor.ExecuteNonQueryAsync(
                connection, null, Dialect, _commandTimeoutSeconds, command, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<TResult> ExecuteAsync<TResult>(IDbCommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var connection = await _provider.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await DbCommandExecutor.ExecuteCommandAsync(
                connection, null, Dialect, _commandTimeoutSeconds, command, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<TRow>> QueryAsync<TRow>(IDbQuery<TRow> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var connection = await _provider.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await DbCommandExecutor.ExecuteQueryAsync(
                connection, null, Dialect, _commandTimeoutSeconds, query, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ExecuteTransactionAsync(
        Func<IDbTransaction, CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var transaction = await OpenTransactionScopeAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                await work(transaction, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                transaction.RecordFailure(exception);
                throw;
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<TResult> ExecuteTransactionAsync<TResult>(
        Func<IDbTransaction, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var transaction = await OpenTransactionScopeAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            TResult result;
            try
            {
                result = await work(transaction, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                transaction.RecordFailure(exception);
                throw;
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
    }

    public async Task<IDbTransaction> OpenTransactionAsync(CancellationToken cancellationToken = default) =>
        await OpenTransactionScopeAsync(cancellationToken).ConfigureAwait(false);

    private async Task<DbTransactionScope> OpenTransactionScopeAsync(CancellationToken cancellationToken)
    {
        var connection = await _provider.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        DbTransaction? transaction = null;
        try
        {
            transaction = await BeginTransactionAsync(connection, cancellationToken).ConfigureAwait(false);
            return new DbTransactionScope(connection, transaction, _provider.DatabaseProvider, _provider.Dialect, _commandTimeoutSeconds);
        }
        catch
        {
            if (transaction is not null)
                await transaction.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask<DbTransaction> BeginTransactionAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                throw new OperationCanceledException("Transaction opening was canceled.", cancellationToken);
            }
            return transaction;
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested && exception.CancellationToken != cancellationToken)
        {
            throw new OperationCanceledException("Transaction opening was canceled.", exception, cancellationToken);
        }
    }
}
