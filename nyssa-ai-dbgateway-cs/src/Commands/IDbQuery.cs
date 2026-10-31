namespace NyssaAI.DbGateway.Commands;

/// <summary>Defines a reusable typed database query that maps each result row into an immutable item.</summary>
/// <typeparam name="TRow">The item type mapped for each row.</typeparam>
public interface IDbQuery<out TRow>
{
    /// <summary>Returns the parameterized SQL query for the active database dialect.</summary>
    /// <param name="dialect">The active database dialect.</param>
    /// <returns>A non-empty SQL query string.</returns>
    public string GetSql(DbDialect dialect);

    /// <summary>Binds query parameters with automatic dialect type coercion.</summary>
    /// <param name="parameters">The parameter binder for the underlying database command.</param>
    /// <param name="dialect">The active database dialect.</param>
    public void BindParameters(IParameterBinder parameters, DbDialect dialect);

    /// <summary>Maps the current row of the reader into an item instance.</summary>
    /// <param name="reader">The dialect-normalizing data record reader positioned on the current row.</param>
    /// <returns>The mapped row item.</returns>
    public TRow MapRow(IDataRecordReader reader);
}
