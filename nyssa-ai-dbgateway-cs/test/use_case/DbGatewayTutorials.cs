using Microsoft.Extensions.DependencyInjection;
using NyssaAI.DbGateway.Configuration;
using NyssaAI.DbGateway.Migrations;
using NyssaAI.DbGateway.Providers;
using NyssaAI.DbGateway.UseCaseTests.Domain;
using NyssaAI.DbGateway.UseCaseTests.Fixtures;
using Xunit;

namespace NyssaAI.DbGateway.UseCaseTests;

[Collection("Database")]
public sealed class DbGatewayTutorials
{
    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    public async Task ExecuteAndQueryAsync_SameTypedCommands_WorkSeamlesslyAcrossSqliteAndPostgreSql(string providerName)
    {
        using var workspace = new DatabaseWorkspace();
        var services = new ServiceCollection();
        services.AddNyssaDbGateway(
            new DbGatewayConfiguration
            {
                DatabaseProvider = providerName,
                ConnectionString = workspace.ConnectionString(providerName)
            },
            migrations: DatabaseWorkspace.TaskCatalog);

        await using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<IDbProvider>();
        await workspace.ResetSchemaAsync(provider, TestContext.Current.CancellationToken);

        var migrations = container.GetRequiredService<IMigrationRunner>();
        await migrations.MigrateAsync("1.1.0", TestContext.Current.CancellationToken);

        var gateway = container.GetRequiredService<IDbGateway>();
        var taskId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 10, 31, 14, 15, 0, TimeSpan.Zero);

        var affected = await gateway.ExecuteAsync(new TaskCreateCommand
        {
            Id = taskId,
            ProjectId = 100L,
            Title = "Switch database seamlessly",
            IsCompleted = false,
            CreatedAt = createdAt,
            Note = "Works on both engines"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(1, affected);

        var loaded = await gateway.ExecuteAsync(new TaskGetByIdCommand { Id = taskId }, TestContext.Current.CancellationToken);
        Assert.NotNull(loaded);
        Assert.Equal(taskId, loaded.Id);
        Assert.Equal(100L, loaded.ProjectId);
        Assert.Equal("Switch database seamlessly", loaded.Title);
        Assert.False(loaded.IsCompleted);
        Assert.Equal(createdAt, loaded.CreatedAt);
        Assert.Equal("Works on both engines", loaded.Note);

        var openList = await gateway.QueryAsync(new TaskListOpenByProjectQuery { ProjectId = 100L }, TestContext.Current.CancellationToken);
        Assert.Single(openList);
        Assert.Equal(taskId, openList[0].Id);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    public async Task ExecuteTransactionAsync_MultiStepCommitAndRollback_PreservesAtomicityAcrossEngines(string providerName)
    {
        using var workspace = new DatabaseWorkspace();
        var services = new ServiceCollection();
        services.AddNyssaDbGateway(
            new DbGatewayConfiguration
            {
                DatabaseProvider = providerName,
                ConnectionString = workspace.ConnectionString(providerName)
            },
            migrations: DatabaseWorkspace.TaskCatalog);

        await using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<IDbProvider>();
        await workspace.ResetSchemaAsync(provider, TestContext.Current.CancellationToken);

        var migrations = container.GetRequiredService<IMigrationRunner>();
        await migrations.MigrateAsync("1.1.0", TestContext.Current.CancellationToken);

        var gateway = container.GetRequiredService<IDbGateway>();
        var taskId = Guid.NewGuid();
        await gateway.ExecuteAsync(new TaskCreateCommand
        {
            Id = taskId,
            ProjectId = 200L,
            Title = "Complete with audit",
            IsCompleted = false,
            CreatedAt = DateTimeOffset.UtcNow
        }, TestContext.Current.CancellationToken);

        var auditId = await gateway.ExecuteTransactionAsync(async (tx, ct) =>
        {
            await tx.ExecuteAsync(new TaskCompleteCommand { Id = taskId }, ct);
            return await tx.ExecuteAsync(new AuditInsertReturningIdCommand
            {
                TaskId = taskId,
                Action = "completed"
            }, ct);
        }, TestContext.Current.CancellationToken);

        Assert.True(auditId > 0);
        var completedTask = await gateway.ExecuteAsync(new TaskGetByIdCommand { Id = taskId }, TestContext.Current.CancellationToken);
        Assert.NotNull(completedTask);
        Assert.True(completedTask.IsCompleted);

        var rolledBackTaskId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await gateway.ExecuteTransactionAsync(async (tx, ct) =>
            {
                await tx.ExecuteAsync(new TaskCreateCommand
                {
                    Id = rolledBackTaskId,
                    ProjectId = 200L,
                    Title = "Will rollback",
                    IsCompleted = false,
                    CreatedAt = DateTimeOffset.UtcNow
                }, ct);
                throw new InvalidOperationException("Abort transaction");
            }, TestContext.Current.CancellationToken);
        });

        var missingTask = await gateway.ExecuteAsync(new TaskGetByIdCommand { Id = rolledBackTaskId }, TestContext.Current.CancellationToken);
        Assert.Null(missingTask);
    }
}
