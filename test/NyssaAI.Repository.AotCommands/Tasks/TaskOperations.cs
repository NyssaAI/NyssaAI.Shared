using NyssaAI.Repository.Commands;
namespace NyssaAI.Repository.AotCommands.Tasks;
/// <summary>TaskOperations The typed keys and construction registrations exposed by the application.</summary>
public static class TaskOperations
{
    /// <summary>Gets the create operation returning the persisted task.</summary>
    public static RepoCommandKey<TaskParameters, TaskRow> Create { get; } = new() { Name = "tasks.create" };
    /// <summary>Gets the write operation key.</summary>
    public static RepoCommandKey<TaskParameters, int> Write { get; } = new() { Name = "tasks.write" };
    /// <summary>Gets the exactly-one title lookup key.</summary>
    public static RepoCommandKey<TaskParameters, TaskRow> Find { get; } = new() { Name = "tasks.find" };
    /// <summary>Gets the scalar count key.</summary>
    public static RepoCommandKey<TaskParameters, long> Count { get; } = new() { Name = "tasks.count" };
    /// <summary>Gets the buffered list key.</summary>
    public static RepoCommandKey<TaskParameters, IReadOnlyList<TaskRow>> List { get; } = new() { Name = "tasks.list" };
    /// <summary>Gets the application composite key.</summary>
    public static RepoCommandKey<TaskParameters, TaskReport> Report { get; } = new() { Name = "tasks.report" };
    /// <summary>Gets the later-query failure fixture key.</summary>
    public static RepoCommandKey<TaskParameters, TaskReport> ReportFailure { get; } = new() { Name = "tasks.report-failure" };
    /// <summary>Gets the PostgreSQL in-flight cancellation fixture key.</summary>
    public static RepoCommandKey<TaskParameters, int> Delay { get; } = new() { Name = "tasks.delay" };
    /// <summary>Gets the delete operation key.</summary>
    public static RepoCommandKey<TaskParameters, int> Delete { get; } = new() { Name = "tasks.delete" };
    /// <summary>Gets the invalid row conversion fixture key.</summary>
    public static RepoCommandKey<TaskParameters, TaskRow> Malformed { get; } = new() { Name = "tasks.malformed" };
    /// <summary>Gets the transactional notification insert key.</summary>
    public static RepoCommandKey<TaskParameters, int> Outbox { get; } = new() { Name = "tasks.outbox" };
    /// <summary>Creates compiled command factories for the selected provider.</summary>
    public static IReadOnlyList<RepoCommandRegistration> Register(string provider) =>
    [
        RepoCommandRegistration.Create(provider, Create, parameters => NyssaAI.Results.Result<IRepoCommand<TaskParameters, TaskRow>>.Success(new TaskCreate(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, Write, parameters => NyssaAI.Results.Result<IRepoCommand<TaskParameters, int>>.Success(new TaskWrite(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, Find, parameters => NyssaAI.Results.Result<IRepoCommand<TaskParameters, TaskRow>>.Success(new TaskFind(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, Count, parameters => NyssaAI.Results.Result<IRepoCommand<TaskParameters, long>>.Success(new TaskCount(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, List, parameters => NyssaAI.Results.Result<IRepoCommand<TaskParameters, IReadOnlyList<TaskRow>>>.Success(new TaskList(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, Report, parameters => NyssaAI.Results.Result<IRepoCommand<TaskParameters, TaskReport>>.Success(new TaskReportQuery(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, ReportFailure, parameters => NyssaAI.Results.Result<IRepoCommand<TaskParameters, TaskReport>>.Success(new TaskReportFailure(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, Delay, parameters => NyssaAI.Results.Result<IRepoCommand<TaskParameters, int>>.Success(new TaskDelay(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, Delete, parameters => NyssaAI.Results.Result<IRepoCommand<TaskParameters, int>>.Success(new TaskDelete(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, Malformed, parameters => NyssaAI.Results.Result<IRepoCommand<TaskParameters, TaskRow>>.Success(new TaskMalformed(parameters))).ThrowIfError(),
        RepoCommandRegistration.Create(provider, Outbox, parameters => NyssaAI.Results.Result<IRepoCommand<TaskParameters, int>>.Success(new TaskOutboxWrite(parameters))).ThrowIfError(),
    ];
}
