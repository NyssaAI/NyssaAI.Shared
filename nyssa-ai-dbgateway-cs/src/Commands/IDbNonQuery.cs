namespace NyssaAI.DbGateway.Commands;

/// <summary>Defines a reusable database write operation that returns the number of affected rows.</summary>
public interface IDbNonQuery
{
    /// <summary>Returns the parameterized SQL statement for the active database dialect.</summary>
    /// <param name="dialect">The active database dialect.</param>
    /// <returns>A non-empty SQL statement.</returns>
    public string GetSql(DbDialect dialect);

    /// <summary>Binds operation parameters with automatic dialect type coercion.</summary>
    /// <param name="parameters">The parameter binder for the underlying database command.</param>
    /// <param name="dialect">The active database dialect.</param>
    public void BindParameters(IParameterBinder parameters, DbDialect dialect);
}
