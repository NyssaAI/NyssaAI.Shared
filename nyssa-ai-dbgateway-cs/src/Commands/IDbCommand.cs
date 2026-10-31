namespace NyssaAI.DbGateway.Commands;

/// <summary>Defines a reusable typed database command that reads and materializes a single result.</summary>
/// <typeparam name="TResult">The result type returned by the command.</typeparam>
public interface IDbCommand<TResult>
{
    /// <summary>Returns the parameterized SQL statement for the active database dialect.</summary>
    /// <param name="dialect">The active database dialect.</param>
    /// <returns>A non-empty SQL statement.</returns>
    public string GetSql(DbDialect dialect);

    /// <summary>Binds operation parameters with automatic dialect type coercion.</summary>
    /// <param name="parameters">The parameter binder for the underlying database command.</param>
    /// <param name="dialect">The active database dialect.</param>
    public void BindParameters(IParameterBinder parameters, DbDialect dialect);

    /// <summary>Materializes the command result from the open data reader.</summary>
    /// <param name="reader">The dialect-normalizing data record reader positioned before the first row.</param>
    /// <param name="cancellationToken">Cancels asynchronous row reading.</param>
    /// <returns>The materialized result.</returns>
    public ValueTask<TResult> MapResultAsync(IDataRecordReader reader, CancellationToken cancellationToken = default);
}
