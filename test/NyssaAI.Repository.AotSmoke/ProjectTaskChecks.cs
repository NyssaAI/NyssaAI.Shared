using NyssaAI.Repository.AotCommands.ProjectTasks;
using NyssaAI.Repository.CommandPipelines;
using NyssaAI.Repository.Providers;
using NyssaAI.Results;

namespace NyssaAI.Repository.AotSmoke;

// Native coverage of every concrete command used by RepositoryTutorials, on both providers.
internal static class ProjectTaskChecks
{
    internal static async Task<int> RunAsync(IRepository repository, IRepositoryProvider provider)
    {
        await using var observer = await provider.OpenConnectionAsync().UnwrapAsync();
        await using (var setup = observer.CreateCommand())
        {
            setup.CommandText = """
                DROP TABLE IF EXISTS tutorial_task_transfers;
                DROP TABLE IF EXISTS tutorial_tasks;
                DROP TABLE IF EXISTS tutorial_projects;
                CREATE TABLE tutorial_projects (id BIGINT PRIMARY KEY, name TEXT NOT NULL);
                CREATE TABLE tutorial_tasks (
                    id BIGINT PRIMARY KEY, project_id BIGINT NOT NULL REFERENCES tutorial_projects(id),
                    title TEXT NOT NULL, note TEXT NULL, is_completed BOOLEAN NOT NULL);
                CREATE TABLE tutorial_task_transfers (
                    transfer_id BIGINT PRIMARY KEY,
                    source_project_id BIGINT NOT NULL REFERENCES tutorial_projects(id),
                    destination_project_id BIGINT NOT NULL REFERENCES tutorial_projects(id), tasks_moved BIGINT NOT NULL);
                INSERT INTO tutorial_projects (id, name) VALUES (10, 'Website launch'), (20, 'Next release');
                """;
            await setup.ExecuteNonQueryAsync();
        }

        IReadOnlyList<TaskSummary> openTasks;
        await using (var session = await repository.OpenSessionAsync().UnwrapAsync())
        {
            var create = repository.CommandRegistry(ProjectTaskOperations.Create, new TaskCreateParameters
            {
                Id = 1, ProjectId = 10, Title = "Write release notes", Note = null
            }).Unwrap();
            var saved = await session.ExecuteAsync(create).UnwrapAsync();
            if (saved.Id != 1 || saved.ProjectId != 10 || saved.Title != "Write release notes" || saved.Note is not null || saved.IsCompleted)
                return Fail("insert-dto");

            var get = repository.CommandRegistry(ProjectTaskOperations.Get, new TaskIdParameters { Id = 1 }).Unwrap();
            TaskDetails details = await session.ExecuteAsync(get).UnwrapAsync();
            var card = (TaskCard)details;
            if (card.TaskId != 1 || card.Heading != "Write release notes") return Fail("get-and-cast");

            var rename = repository.CommandRegistry(ProjectTaskOperations.Rename,
                new TaskRenameParameters { Id = 1, Title = "Publish release notes" }).Unwrap();
            var renamed = await session.ExecuteAsync(rename).UnwrapAsync();
            if (renamed.Title != "Publish release notes" || renamed.IsCompleted) return Fail("update-dto");

            var project = new ProjectTaskParameters { ProjectId = 10 };
            var list = repository.CommandRegistry(ProjectTaskOperations.ListOpen, project).Unwrap();
            openTasks = await session.ExecuteAsync(list).UnwrapAsync();
            var count = repository.CommandRegistry(ProjectTaskOperations.CountOpen, project).Unwrap();
            if (await session.ExecuteAsync(count).UnwrapAsync() != 1L) return Fail("scalar");
            var progressCommand = repository.CommandRegistry(ProjectTaskOperations.Progress, project).Unwrap();
            var progress = await session.ExecuteAsync(progressCommand).UnwrapAsync();
            if (progress.ProjectId != 10 || progress.TotalTasks != 1 || progress.CompletedTasks != 0 || progress.RemainingTasks != 1)
                return Fail("aggregate-dto");
            var missing = repository.CommandRegistry(ProjectTaskOperations.Get, new TaskIdParameters { Id = 404 }).Unwrap();
            var missingResult = await session.ExecuteAsync(missing);
            if (missingResult.IsSuccess || missingResult.Errors[0].Category != ErrorCategory.NotFound) return Fail("not-found");
        }
        if (openTasks.Count != 1 || openTasks[0].Title != "Publish release notes") return Fail("buffered-list");

        await using (var transaction = await repository.OpenTransactionAsync().UnwrapAsync())
        {
            var followUp = await transaction
                .Pipeline(repository, ProjectTaskOperations.Complete, new TaskIdParameters { Id = 1 })
                .Then(ProjectTaskOperations.Create, completed => new TaskCreateParameters
                {
                    Id = 2, ProjectId = completed.ProjectId, Title = "Announce release", Note = completed.Title
                }).ExecuteAsync().UnwrapAsync();
            if (followUp.Id != 2 || followUp.Note != "Publish release notes" || followUp.IsCompleted) return Fail("pipeline");
            if ((await transaction.CommitAsync()).IsFailure) return Fail("pipeline-commit");
        }
        await using (var transaction = await repository.OpenTransactionAsync().UnwrapAsync())
        {
            var move = repository.CommandRegistry(ProjectTaskOperations.MoveOpen,
                new TaskTransferParameters { TransferId = 100, SourceProjectId = 10, DestinationProjectId = 20 }).Unwrap();
            var moved = await transaction.ExecuteAsync(move).UnwrapAsync();
            var record = repository.CommandRegistry(ProjectTaskOperations.RecordTransfer, moved).Unwrap();
            var audit = await transaction.ExecuteAsync(record).UnwrapAsync();
            if (audit.TransferId != 100 || audit.SourceProjectId != 10 || audit.DestinationProjectId != 20 || audit.TasksMoved != 1)
                return Fail("transfer-dto");
            if ((await transaction.CommitAsync()).IsFailure) return Fail("transfer-commit");
        }
        await using (var transaction = await repository.OpenTransactionAsync().UnwrapAsync())
        {
            var move = repository.CommandRegistry(ProjectTaskOperations.MoveOpen,
                new TaskTransferParameters { TransferId = 100, SourceProjectId = 20, DestinationProjectId = 10 }).Unwrap();
            var moved = await transaction.ExecuteAsync(move).UnwrapAsync();
            if (moved.TasksMoved != 1) return Fail("rollback-precondition");
            var duplicate = repository.CommandRegistry(ProjectTaskOperations.RecordTransfer, moved).Unwrap();
            if ((await transaction.ExecuteAsync(duplicate)).IsSuccess) return Fail("duplicate-audit");
            if ((await transaction.CommitAsync()).IsSuccess) return Fail("failed-transfer-commit");
        }
        await using (var check = observer.CreateCommand())
        {
            check.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM tutorial_tasks WHERE id = 1 AND project_id = 10 AND is_completed = TRUE) +
                    (SELECT COUNT(*) FROM tutorial_tasks WHERE id = 2 AND project_id = 20 AND is_completed = FALSE) +
                    (SELECT COUNT(*) FROM tutorial_task_transfers WHERE transfer_id = 100 AND source_project_id = 10 AND destination_project_id = 20 AND tasks_moved = 1)
                """;
            if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 3) return Fail("independent-persistence");
        }
        Console.WriteLine($"PASS {provider.DatabaseProvider} tutorial-dtos-scalars-cast-pipeline-transfer-rollback");
        return 0;
    }

    private static int Fail(string scenario)
    {
        Console.Error.WriteLine($"FAIL tutorial-{scenario}");
        return 1;
    }
}
