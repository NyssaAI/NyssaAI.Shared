using NyssaAI.Repository.AotCommands.ProjectTasks;
using NyssaAI.Repository.CommandPipelines;
using NyssaAI.Repository.UseCaseTests.fixtures;
using Xunit;

namespace NyssaAI.Repository.UseCaseTests;

// Each test starts with an empty database. The fixture owns setup; the test shows the caller's workflow.
public sealed class RepositoryTutorials : IClassFixture<TaskSavingFixture>, IAsyncLifetime
{
    // private state
    private readonly TaskSavingFixture _fixture;

    // constructor
    public RepositoryTutorials(TaskSavingFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ExecuteAsync_CreateTask_PersistsTaskAsync()
    {
        // Arrange
        var repository = _fixture.Repository;
        var cancellationToken = TestContext.Current.CancellationToken;
        await _fixture.AddProjectAsync(10, "Website launch", cancellationToken);
        var parameters = new TaskCreateParameters
        {
            Id = 1,
            ProjectId = 10,
            Title = "Write release notes",
            Note = "Include the new search feature",
        };
        TaskDetails savedTask;

        // Act
        await using (var session = await repository.OpenSessionAsync(cancellationToken))
        {
            // The key fixes the DTO returned by execution.
            savedTask = await session.ExecuteAsync(
                repository.CommandRegistry(ProjectTaskOperations.Create, parameters),
                cancellationToken
            );
        }

        var persistedTask = await _fixture.ReadTaskAsync(1, cancellationToken);

        // Assert
        Assert.Equal(1, savedTask.Id);
        Assert.Equal(10, savedTask.ProjectId);
        Assert.Equal("Write release notes", savedTask.Title);
        Assert.Equal("Include the new search feature", savedTask.Note);
        Assert.False(savedTask.IsCompleted);
        Assert.Equal("Write release notes", persistedTask.Title);
        Assert.Equal("Include the new search feature", persistedTask.Note);
        Assert.Equal(10, persistedTask.ProjectId);
        Assert.False(persistedTask.IsCompleted);
    }

    [Fact]
    public async Task ExecuteAsync_ExistingTask_ReturnsDetailsAsync()
    {
        // Arrange
        var repository = _fixture.Repository;
        var cancellationToken = TestContext.Current.CancellationToken;
        await _fixture.AddProjectAsync(10, "Website launch", cancellationToken);
        await _fixture.AddTaskAsync(1, 10, "Write release notes", null, false, cancellationToken);
        TaskDetails task;

        // Act
        await using (var session = await repository.OpenSessionAsync(cancellationToken))
        {
            task = await session.ExecuteAsync(
                repository.CommandRegistry(ProjectTaskOperations.Get, new TaskIdParameters { Id = 1 }),
                cancellationToken
            );
        }

        // Assert
        Assert.Equal(1, task.Id);
        Assert.Equal(10, task.ProjectId);
        Assert.Equal("Write release notes", task.Title);
        Assert.Null(task.Note);
        Assert.False(task.IsCompleted);
    }

    [Fact]
    public async Task ExecuteAsync_OpenTasks_ReturnsSummariesAsync()
    {
        // Arrange
        var repository = _fixture.Repository;
        var cancellationToken = TestContext.Current.CancellationToken;
        await _fixture.AddProjectAsync(10, "Website launch", cancellationToken);
        await _fixture.AddProjectAsync(20, "Mobile launch", cancellationToken);
        await _fixture.AddTaskAsync(1, 10, "Write release notes", null, false, cancellationToken);
        await _fixture.AddTaskAsync(2, 10, "Review screenshots", null, false, cancellationToken);
        await _fixture.AddTaskAsync(3, 10, "Approve design", null, true, cancellationToken);
        await _fixture.AddTaskAsync(4, 20, "Test mobile navigation", null, false, cancellationToken);
        IReadOnlyList<TaskSummary> tasks;

        // Act
        await using (var session = await repository.OpenSessionAsync(cancellationToken))
        {
            tasks = await session.ExecuteAsync(
                repository.CommandRegistry(
                    ProjectTaskOperations.ListOpen,
                    new ProjectTaskParameters { ProjectId = 10 }
                ),
                cancellationToken
            );
        }

        // Assert
        // The list is buffered, so its DTOs remain usable after the session closes.
        Assert.Equal(2, tasks.Count);
        Assert.Equal(1, tasks[0].Id);
        Assert.Equal("Write release notes", tasks[0].Title);
        Assert.Equal(2, tasks[1].Id);
        Assert.Equal("Review screenshots", tasks[1].Title);
    }

    [Fact]
    public async Task ExecuteAsync_RenameTask_PersistsUpdatedDetailsAsync()
    {
        // Arrange
        var repository = _fixture.Repository;
        var cancellationToken = TestContext.Current.CancellationToken;
        await _fixture.AddProjectAsync(10, "Website launch", cancellationToken);
        await _fixture.AddTaskAsync(1, 10, "Draft release notes", "Include screenshots", false, cancellationToken);
        TaskDetails renamedTask;

        // Act
        await using (var session = await repository.OpenSessionAsync(cancellationToken))
        {
            renamedTask = await session.ExecuteAsync(
                repository.CommandRegistry(
                    ProjectTaskOperations.Rename,
                    new TaskRenameParameters { Id = 1, Title = "Publish release notes" }
                ),
                cancellationToken
            );
        }

        var persistedTask = await _fixture.ReadTaskAsync(1, cancellationToken);

        // Assert
        Assert.Equal(1, renamedTask.Id);
        Assert.Equal(10, renamedTask.ProjectId);
        Assert.Equal("Publish release notes", renamedTask.Title);
        Assert.Equal("Include screenshots", renamedTask.Note);
        Assert.False(renamedTask.IsCompleted);
        Assert.Equal("Publish release notes", persistedTask.Title);
    }

    [Fact]
    public async Task ExecuteAsync_OpenTasks_ReturnsCountAsync()
    {
        // Arrange
        var repository = _fixture.Repository;
        var cancellationToken = TestContext.Current.CancellationToken;
        await _fixture.AddProjectAsync(10, "Website launch", cancellationToken);
        await _fixture.AddProjectAsync(20, "Mobile launch", cancellationToken);
        await _fixture.AddTaskAsync(1, 10, "Write release notes", null, false, cancellationToken);
        await _fixture.AddTaskAsync(2, 10, "Review screenshots", null, false, cancellationToken);
        await _fixture.AddTaskAsync(3, 10, "Approve design", null, true, cancellationToken);
        await _fixture.AddTaskAsync(4, 20, "Test mobile navigation", null, false, cancellationToken);
        long openTaskCount;

        // Act
        await using (var session = await repository.OpenSessionAsync(cancellationToken))
        {
            openTaskCount = await session.ExecuteAsync(
                repository.CommandRegistry(
                    ProjectTaskOperations.CountOpen,
                    new ProjectTaskParameters { ProjectId = 10 }
                ),
                cancellationToken
            );
        }

        // Assert
        Assert.Equal(2L, openTaskCount);
    }

    [Fact]
    public async Task ExecuteAsync_ProjectProgress_ReturnsSummaryAsync()
    {
        // Arrange
        var repository = _fixture.Repository;
        var cancellationToken = TestContext.Current.CancellationToken;
        await _fixture.AddProjectAsync(10, "Website launch", cancellationToken);
        await _fixture.AddTaskAsync(1, 10, "Write release notes", null, false, cancellationToken);
        await _fixture.AddTaskAsync(2, 10, "Review screenshots", null, false, cancellationToken);
        await _fixture.AddTaskAsync(3, 10, "Approve design", null, true, cancellationToken);
        ProjectProgress progress;

        // Act
        await using (var session = await repository.OpenSessionAsync(cancellationToken))
        {
            progress = await session.ExecuteAsync(
                repository.CommandRegistry(
                    ProjectTaskOperations.Progress,
                    new ProjectTaskParameters { ProjectId = 10 }
                ),
                cancellationToken
            );
        }

        // Assert
        Assert.Equal(10, progress.ProjectId);
        Assert.Equal(3L, progress.TotalTasks);
        Assert.Equal(1L, progress.CompletedTasks);
        Assert.Equal(2L, progress.RemainingTasks);
    }

    [Fact]
    public async Task ExecuteAsync_TaskDetails_MapsToCallerTypeAsync()
    {
        // Arrange
        var repository = _fixture.Repository;
        var cancellationToken = TestContext.Current.CancellationToken;
        await _fixture.AddProjectAsync(10, "Website launch", cancellationToken);
        await _fixture.AddTaskAsync(1, 10, "Write release notes", null, false, cancellationToken);
        TaskCard card;

        // Act
        await using (var session = await repository.OpenSessionAsync(cancellationToken))
        {
            var findCommand = repository.CommandRegistry(ProjectTaskOperations.Get, new TaskIdParameters { Id = 1 });
            TaskDetails task = await session.ExecuteAsync(findCommand, cancellationToken);

            // This cast uses the application-defined conversion operator in TaskCard.
            card = (TaskCard)task;
        }

        // Assert
        Assert.Equal(1, card.TaskId);
        Assert.Equal("Write release notes", card.Heading);
    }

    [Fact]
    public async Task ExecuteAsync_MissingTask_ReturnsNotFoundAsync()
    {
        // Arrange
        var repository = _fixture.Repository;
        var cancellationToken = TestContext.Current.CancellationToken;
        KeyNotFoundException missingTask;

        // Act
        await using (var session = await repository.OpenSessionAsync(cancellationToken))
        {
            var findCommand = repository.CommandRegistry(ProjectTaskOperations.Get, new TaskIdParameters { Id = 404 });
            // A missing required row raises a KeyNotFoundException.
            missingTask = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                session.ExecuteAsync(findCommand, cancellationToken)
            );
        }

        // Assert

        Assert.Equal("Task not found.", missingTask.Message);
    }

    [Fact]
    public async Task ExecuteAsync_CompleteTaskAndFollowUp_CommitsTogetherAsync()
    {
        // Arrange
        var repository = _fixture.Repository;
        var cancellationToken = TestContext.Current.CancellationToken;
        await _fixture.AddProjectAsync(10, "Website launch", cancellationToken);
        await _fixture.AddTaskAsync(1, 10, "Write release notes", null, false, cancellationToken);
        TaskDetails followUp;

        // Act
        await using (var transaction = await repository.OpenTransactionAsync(cancellationToken))
        {
            followUp = await transaction
                .Pipeline(repository, ProjectTaskOperations.Complete, new TaskIdParameters { Id = 1 })
                .Then(
                    ProjectTaskOperations.Create,
                    completedTask => new TaskCreateParameters
                    {
                        Id = 2,
                        ProjectId = completedTask.ProjectId,
                        Title = "Publish release notes",
                        Note = $"Follow up on: {completedTask.Title}",
                    }
                )
                .ExecuteAsync(cancellationToken);

            // The pipeline shares this transaction; it does not commit for us.
            await transaction.CommitAsync(cancellationToken);
        }

        var completedTask = await _fixture.ReadTaskAsync(1, cancellationToken);
        var persistedFollowUp = await _fixture.ReadTaskAsync(2, cancellationToken);

        // Assert

        Assert.Equal(2, followUp.Id);
        Assert.Equal(10, followUp.ProjectId);
        Assert.Equal("Publish release notes", followUp.Title);
        Assert.Equal("Follow up on: Write release notes", followUp.Note);
        Assert.False(followUp.IsCompleted);
        Assert.True(completedTask.IsCompleted);
        Assert.Equal("Publish release notes", persistedFollowUp.Title);
        Assert.Equal("Follow up on: Write release notes", persistedFollowUp.Note);
    }

    [Fact]
    public async Task ExecuteAsync_MoveOpenTasks_CommitsTransferAuditAsync()
    {
        // Arrange
        var repository = _fixture.Repository;
        var cancellationToken = TestContext.Current.CancellationToken;
        await _fixture.AddProjectAsync(10, "Website launch", cancellationToken);
        await _fixture.AddProjectAsync(20, "Next release", cancellationToken);
        await _fixture.AddTaskAsync(1, 10, "Write release notes", null, false, cancellationToken);
        await _fixture.AddTaskAsync(2, 10, "Review screenshots", null, false, cancellationToken);
        await _fixture.AddTaskAsync(3, 10, "Approve design", null, true, cancellationToken);
        await _fixture.AddTaskAsync(4, 20, "Plan next release", null, false, cancellationToken);
        IReadOnlyList<TaskSummary> tasksToMove;
        TaskTransferSummary transfer;

        // Act
        await using (var transaction = await repository.OpenTransactionAsync(cancellationToken))
        {
            tasksToMove = await transaction.ExecuteAsync(
                repository.CommandRegistry(
                    ProjectTaskOperations.ListOpen,
                    new ProjectTaskParameters { ProjectId = 10 }
                ),
                cancellationToken
            );

            var moveCommand = repository.CommandRegistry(
                ProjectTaskOperations.MoveOpen,
                new TaskTransferParameters
                {
                    TransferId = 100,
                    SourceProjectId = 10,
                    DestinationProjectId = 20,
                }
            );
            TaskTransferSummary movedTasks = await transaction.ExecuteAsync(moveCommand, cancellationToken);

            // Persist the actual move count in the same transaction as the moves.
            transfer = await transaction.ExecuteAsync(
                repository.CommandRegistry(ProjectTaskOperations.RecordTransfer, movedTasks),
                cancellationToken
            );
            await transaction.CommitAsync(cancellationToken);
        }

        var audit = await _fixture.ReadTransferAsync(100, cancellationToken);
        var storedTask1 = await _fixture.ReadTaskAsync(1, cancellationToken);
        var storedTask2 = await _fixture.ReadTaskAsync(2, cancellationToken);
        var storedTask3 = await _fixture.ReadTaskAsync(3, cancellationToken);
        var storedTask4 = await _fixture.ReadTaskAsync(4, cancellationToken);
        var transferCount = await _fixture.CountTransfersAsync(cancellationToken);

        // Assert

        Assert.Equal(2, tasksToMove.Count);
        Assert.Equal("Write release notes", tasksToMove[0].Title);
        Assert.Equal("Review screenshots", tasksToMove[1].Title);
        Assert.Equal(100, transfer.TransferId);
        Assert.Equal(10, transfer.SourceProjectId);
        Assert.Equal(20, transfer.DestinationProjectId);
        Assert.Equal(2L, transfer.TasksMoved);
        Assert.Equal(20, storedTask1.ProjectId);
        Assert.Equal(20, storedTask2.ProjectId);
        Assert.Equal(10, storedTask3.ProjectId);
        Assert.Equal(20, storedTask4.ProjectId);
        Assert.Equal(10, audit.SourceProjectId);
        Assert.Equal(20, audit.DestinationProjectId);
        Assert.Equal(2L, audit.TasksMoved);
        Assert.Equal(1L, transferCount);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateTransfer_RollsBackChangesAsync()
    {
        // Arrange
        var repository = _fixture.Repository;
        var cancellationToken = TestContext.Current.CancellationToken;
        await _fixture.AddProjectAsync(10, "Website launch", cancellationToken);
        await _fixture.AddProjectAsync(20, "Next release", cancellationToken);
        await _fixture.AddTaskAsync(1, 10, "Write release notes", null, false, cancellationToken);
        await _fixture.AddTaskAsync(2, 10, "Review screenshots", null, false, cancellationToken);
        await _fixture.AddTaskAsync(3, 10, "Approve design", null, true, cancellationToken);
        // Reusing this transfer ID will fail the audit insert's primary-key constraint.
        await _fixture.AddTransferAsync(100, 10, 20, 7, cancellationToken);
        TaskTransferSummary movedTasks;
        System.Data.Common.DbException auditResult;
        InvalidOperationException commitResult;

        // Act
        await using (var transaction = await repository.OpenTransactionAsync(cancellationToken))
        {
            movedTasks = await transaction.ExecuteAsync(
                repository.CommandRegistry(
                    ProjectTaskOperations.MoveOpen,
                    new TaskTransferParameters
                    {
                        TransferId = 100,
                        SourceProjectId = 10,
                        DestinationProjectId = 20,
                    }
                ),
                cancellationToken
            );

            var auditCommand = repository.CommandRegistry(ProjectTaskOperations.RecordTransfer, movedTasks);
            auditResult = await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() =>
                transaction.ExecuteAsync(auditCommand, cancellationToken)
            );

            // A failed command makes this transaction uncommittable. Disposal rolls it back.
            commitResult = await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync(cancellationToken));
        }

        var originalAudit = await _fixture.ReadTransferAsync(100, cancellationToken);
        var storedTask1 = await _fixture.ReadTaskAsync(1, cancellationToken);
        var storedTask2 = await _fixture.ReadTaskAsync(2, cancellationToken);
        var storedTask3 = await _fixture.ReadTaskAsync(3, cancellationToken);
        var transferCount = await _fixture.CountTransfersAsync(cancellationToken);

        // Assert
        Assert.Equal(2L, movedTasks.TasksMoved);

        Assert.Same(auditResult, commitResult.InnerException);
        Assert.Equal(10, storedTask1.ProjectId);
        Assert.Equal(10, storedTask2.ProjectId);
        Assert.Equal(10, storedTask3.ProjectId);
        Assert.Equal(10, originalAudit.SourceProjectId);
        Assert.Equal(20, originalAudit.DestinationProjectId);
        Assert.Equal(7L, originalAudit.TasksMoved);
        Assert.Equal(1L, transferCount);
    }
}
