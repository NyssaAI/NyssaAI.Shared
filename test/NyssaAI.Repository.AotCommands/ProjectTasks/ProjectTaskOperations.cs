using NyssaAI.Repository.Commands;
using NyssaAI.Results;

namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>Typed application operations and their compiled provider registrations.</summary>
public static class ProjectTaskOperations
{
    /// <summary>Inserts an open task and returns its persisted details.</summary>
    public static RepoCommandKey<TaskCreateParameters, TaskDetails> Create { get; } = new() { Name = "tutorial.tasks.create" };

    /// <summary>Reads task details or returns NotFound.</summary>
    public static RepoCommandKey<TaskIdParameters, TaskDetails> Get { get; } = new() { Name = "tutorial.tasks.get" };

    /// <summary>Changes a task title and returns the updated details.</summary>
    public static RepoCommandKey<TaskRenameParameters, TaskDetails> Rename { get; } = new() { Name = "tutorial.tasks.rename" };

    /// <summary>Completes a task and returns its updated details.</summary>
    public static RepoCommandKey<TaskIdParameters, TaskDetails> Complete { get; } = new() { Name = "tutorial.tasks.complete" };

    /// <summary>Returns the open tasks in a project, ordered by identifier.</summary>
    public static RepoCommandKey<ProjectTaskParameters, IReadOnlyList<TaskSummary>> ListOpen { get; } = new() { Name = "tutorial.tasks.listopen" };

    /// <summary>Counts the open tasks in a project.</summary>
    public static RepoCommandKey<ProjectTaskParameters, long> CountOpen { get; } = new() { Name = "tutorial.tasks.countopen" };

    /// <summary>Aggregates the completed and remaining task counts for a project.</summary>
    public static RepoCommandKey<ProjectTaskParameters, ProjectProgress> Progress { get; } = new() { Name = "tutorial.tasks.progress" };

    /// <summary>Moves all currently open tasks and reports the actual affected count.</summary>
    public static RepoCommandKey<TaskTransferParameters, TaskTransferSummary> MoveOpen { get; } = new() { Name = "tutorial.tasks.moveopen" };

    /// <summary>Records the completed task moves in the shared transaction.</summary>
    public static RepoCommandKey<TaskTransferSummary, TaskTransferSummary> RecordTransfer { get; } = new() { Name = "tutorial.tasks.recordtransfer" };

    /// <summary>Registers the tutorial SQL shared by SQLite and PostgreSQL.</summary>
    public static IReadOnlyList<RepoCommandRegistration> Register(string provider) =>
    [
        RepoCommandRegistration.Create(provider, Create, parameters =>
            Result<IRepoCommand<TaskCreateParameters, TaskDetails>>.Success(new TaskInsert(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, Get, parameters =>
            Result<IRepoCommand<TaskIdParameters, TaskDetails>>.Success(new TaskGet(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, Rename, parameters =>
            Result<IRepoCommand<TaskRenameParameters, TaskDetails>>.Success(new TaskRename(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, Complete, parameters =>
            Result<IRepoCommand<TaskIdParameters, TaskDetails>>.Success(new TaskComplete(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, ListOpen, parameters =>
            Result<IRepoCommand<ProjectTaskParameters, IReadOnlyList<TaskSummary>>>.Success(new TaskListOpen(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, CountOpen, parameters =>
            Result<IRepoCommand<ProjectTaskParameters, long>>.Success(new TaskCountOpen(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, Progress, parameters =>
            Result<IRepoCommand<ProjectTaskParameters, ProjectProgress>>.Success(new ProjectProgressGet(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, MoveOpen, parameters =>
            Result<IRepoCommand<TaskTransferParameters, TaskTransferSummary>>.Success(new TaskMoveOpen(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, RecordTransfer, parameters =>
            Result<IRepoCommand<TaskTransferSummary, TaskTransferSummary>>.Success(new TaskTransferRecord(parameters))).ThrowIfError(),
    ];
}
