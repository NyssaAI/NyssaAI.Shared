using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NyssaAI.Repository.AotCommands.ProjectTasks;
using NyssaAI.Repository.Configuration;
using Xunit;

namespace NyssaAI.Repository.UseCaseTests.fixtures;

// Owns the database and setup connection. It never performs the workflow being taught or asserts outcomes.
public sealed class TaskSavingFixture : IAsyncLifetime
{
    private readonly DatabaseWorkspace _database = new();

    public TaskSavingFixture()
    {
        var connectionString = _database.ConnectionString("sqlite") + ";Foreign Keys=True";
        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
        services.AddNyssaRepository(
            new RepositoryConfiguration
            {
                DatabaseProvider = "sqlite",
                ConnectionString = connectionString,
            },
            commands: ProjectTaskOperations.Register("sqlite")
        );
        ApplicationHost = builder.Build();
        SetupConnection = new SqliteConnection(connectionString);
        Repository = Container.GetRequiredService<IRepository>();
    }

    public IHost ApplicationHost { get; }
    public IServiceProvider Container => ApplicationHost.Services;
    public SqliteConnection SetupConnection { get; }
    public IRepository Repository { get; }

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SetupConnection.OpenAsync(cancellationToken);
        await using (var schema = SetupConnection.CreateCommand())
        {
            schema.CommandText = """
                CREATE TABLE tutorial_projects (id BIGINT PRIMARY KEY, name TEXT NOT NULL);
                CREATE TABLE tutorial_tasks (
                    id BIGINT PRIMARY KEY,
                    project_id BIGINT NOT NULL REFERENCES tutorial_projects(id),
                    title TEXT NOT NULL,
                    note TEXT NULL,
                    is_completed BOOLEAN NOT NULL);
                CREATE TABLE tutorial_task_transfers (
                    transfer_id BIGINT PRIMARY KEY,
                    source_project_id BIGINT NOT NULL REFERENCES tutorial_projects(id),
                    destination_project_id BIGINT NOT NULL REFERENCES tutorial_projects(id),
                    tasks_moved BIGINT NOT NULL);
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using (var reset = SetupConnection.CreateCommand())
        {
            reset.CommandText =
                "DELETE FROM tutorial_task_transfers; DELETE FROM tutorial_tasks; DELETE FROM tutorial_projects;";
            await reset.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal async Task AddProjectAsync(long id, string name, CancellationToken cancellationToken)
    {
        await using (var insert = SetupConnection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO tutorial_projects (id, name) VALUES (@Id, @Name)";
            insert.Parameters.AddWithValue("@Id", id);
            insert.Parameters.AddWithValue("@Name", name);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal async Task AddTaskAsync(
        long id,
        long projectId,
        string title,
        string? note,
        bool isCompleted,
        CancellationToken cancellationToken
    )
    {
        await using (var insert = SetupConnection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO tutorial_tasks (id, project_id, title, note, is_completed)
                VALUES (@Id, @ProjectId, @Title, @Note, @IsCompleted)
                """;
            insert.Parameters.AddWithValue("@Id", id);
            insert.Parameters.AddWithValue("@ProjectId", projectId);
            insert.Parameters.AddWithValue("@Title", title);
            insert.Parameters.AddWithValue("@Note", (object?)note ?? DBNull.Value);
            insert.Parameters.AddWithValue("@IsCompleted", isCompleted);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal async Task AddTransferAsync(
        long transferId,
        long sourceProjectId,
        long destinationProjectId,
        long tasksMoved,
        CancellationToken cancellationToken
    )
    {
        await using (var insert = SetupConnection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO tutorial_task_transfers (transfer_id, source_project_id, destination_project_id, tasks_moved)
                VALUES (@TransferId, @SourceProjectId, @DestinationProjectId, @TasksMoved)
                """;
            insert.Parameters.AddWithValue("@TransferId", transferId);
            insert.Parameters.AddWithValue("@SourceProjectId", sourceProjectId);
            insert.Parameters.AddWithValue("@DestinationProjectId", destinationProjectId);
            insert.Parameters.AddWithValue("@TasksMoved", tasksMoved);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal async Task<TaskDetails> ReadTaskAsync(long id, CancellationToken cancellationToken)
    {
        await using (var connection = new SqliteConnection(SetupConnection.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using (var query = connection.CreateCommand())
            {
                query.CommandText =
                    "SELECT id, project_id, title, note, is_completed FROM tutorial_tasks WHERE id = @Id";
                query.Parameters.AddWithValue("@Id", id);
                await using (var row = await query.ExecuteReaderAsync(cancellationToken))
                {
                    if (!await row.ReadAsync(cancellationToken))
                        throw new InvalidOperationException(
                            "The expected persisted task was not found."
                        );
                    return new TaskDetails
                    {
                        Id = row.GetInt64(0),
                        ProjectId = row.GetInt64(1),
                        Title = row.GetString(2),
                        Note = row.IsDBNull(3) ? null : row.GetString(3),
                        IsCompleted = row.GetBoolean(4),
                    };
                }
            }
        }
    }

    internal async Task<TaskTransferSummary> ReadTransferAsync(
        long transferId,
        CancellationToken cancellationToken
    )
    {
        await using (var connection = new SqliteConnection(SetupConnection.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using (var query = connection.CreateCommand())
            {
                query.CommandText = """
                    SELECT transfer_id, source_project_id, destination_project_id, tasks_moved
                    FROM tutorial_task_transfers WHERE transfer_id = @TransferId
                    """;
                query.Parameters.AddWithValue("@TransferId", transferId);
                await using (var row = await query.ExecuteReaderAsync(cancellationToken))
                {
                    if (!await row.ReadAsync(cancellationToken))
                        throw new InvalidOperationException(
                            "The expected persisted transfer was not found."
                        );
                    return new TaskTransferSummary
                    {
                        TransferId = row.GetInt64(0),
                        SourceProjectId = row.GetInt64(1),
                        DestinationProjectId = row.GetInt64(2),
                        TasksMoved = row.GetInt64(3),
                    };
                }
            }
        }
    }

    internal async Task<long> CountTransfersAsync(CancellationToken cancellationToken)
    {
        await using (var connection = new SqliteConnection(SetupConnection.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using (var query = connection.CreateCommand())
            {
                query.CommandText = "SELECT COUNT(*) FROM tutorial_task_transfers";
                return Convert.ToInt64(await query.ExecuteScalarAsync(cancellationToken));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        ApplicationHost.Dispose();
        await SetupConnection.DisposeAsync();
        _database.Dispose();
    }
}
