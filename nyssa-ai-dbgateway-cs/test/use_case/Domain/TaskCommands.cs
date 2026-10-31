using NyssaAI.DbGateway.Commands;

namespace NyssaAI.DbGateway.UseCaseTests.Domain;

public sealed record TaskDetails
{
    public required Guid Id { get; init; }
    public required long ProjectId { get; init; }
    public required string Title { get; init; }
    public required bool IsCompleted { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Note { get; init; }
}

public sealed record TaskCreateCommand : IDbNonQuery
{
    public required Guid Id { get; init; }
    public required long ProjectId { get; init; }
    public required string Title { get; init; }
    public required bool IsCompleted { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Note { get; init; }

    public string GetSql(DbDialect dialect) =>
        "INSERT INTO tutorial_tasks (id, project_id, title, is_completed, created_at, note) VALUES (@id, @projectId, @title, @isCompleted, @createdAt, @note)";

    public void BindParameters(IParameterBinder parameters, DbDialect dialect)
    {
        parameters.Add("@id", Id);
        parameters.Add("@projectId", ProjectId);
        parameters.Add("@title", Title);
        parameters.Add("@isCompleted", IsCompleted);
        parameters.Add("@createdAt", CreatedAt);
        parameters.Add("@note", Note);
    }
}

public sealed record TaskGetByIdCommand : IDbCommand<TaskDetails?>
{
    public required Guid Id { get; init; }

    public string GetSql(DbDialect dialect) =>
        "SELECT id, project_id, title, is_completed, created_at, note FROM tutorial_tasks WHERE id = @id";

    public void BindParameters(IParameterBinder parameters, DbDialect dialect) =>
        parameters.Add("@id", Id);

    public async ValueTask<TaskDetails?> MapResultAsync(IDataRecordReader reader, CancellationToken cancellationToken = default)
    {
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new TaskDetails
        {
            Id = reader.GetGuid(0),
            ProjectId = reader.GetInt64(1),
            Title = reader.GetString(2),
            IsCompleted = reader.GetBoolean(3),
            CreatedAt = reader.GetDateTimeOffset(4),
            Note = reader.GetNullableString(5)
        };
    }
}

public sealed record TaskListOpenByProjectQuery : IDbQuery<TaskDetails>
{
    public required long ProjectId { get; init; }

    public string GetSql(DbDialect dialect) =>
        "SELECT id, project_id, title, is_completed, created_at, note FROM tutorial_tasks WHERE project_id = @projectId AND is_completed = @isCompleted ORDER BY title";

    public void BindParameters(IParameterBinder parameters, DbDialect dialect)
    {
        parameters.Add("@projectId", ProjectId);
        parameters.Add("@isCompleted", false);
    }

    public TaskDetails MapRow(IDataRecordReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            ProjectId = reader.GetInt64(1),
            Title = reader.GetString(2),
            IsCompleted = reader.GetBoolean(3),
            CreatedAt = reader.GetDateTimeOffset(4),
            Note = reader.GetNullableString(5)
        };
}

public sealed record TaskCompleteCommand : IDbNonQuery
{
    public required Guid Id { get; init; }

    public string GetSql(DbDialect dialect) =>
        "UPDATE tutorial_tasks SET is_completed = @isCompleted WHERE id = @id";

    public void BindParameters(IParameterBinder parameters, DbDialect dialect)
    {
        parameters.Add("@id", Id);
        parameters.Add("@isCompleted", true);
    }
}

public sealed record AuditInsertReturningIdCommand : IDbCommand<long>
{
    public required Guid TaskId { get; init; }
    public required string Action { get; init; }

    public string GetSql(DbDialect dialect) => dialect switch
    {
        DbDialect.Sqlite => "INSERT INTO tutorial_audit (task_id, action) VALUES (@taskId, @action); SELECT last_insert_rowid();",
        DbDialect.PostgreSql => "INSERT INTO tutorial_audit (task_id, action) VALUES (@taskId, @action) RETURNING id;",
        _ => throw new NotSupportedException()
    };

    public void BindParameters(IParameterBinder parameters, DbDialect dialect)
    {
        parameters.Add("@taskId", TaskId);
        parameters.Add("@action", Action);
    }

    public async ValueTask<long> MapResultAsync(IDataRecordReader reader, CancellationToken cancellationToken = default)
    {
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Expected generated audit ID.");
        return reader.GetInt64(0);
    }
}
