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
        RepoCommandRegistration.Create(provider, Create, parameters => new TaskCreate(parameters)),
        RepoCommandRegistration.Create(provider, Write, parameters => new TaskWrite(parameters)),
        RepoCommandRegistration.Create(provider, Find, parameters => new TaskFind(parameters)),
        RepoCommandRegistration.Create(provider, Count, parameters => new TaskCount(parameters)),
        RepoCommandRegistration.Create(provider, List, parameters => new TaskList(parameters)),
        RepoCommandRegistration.Create(provider, Report, parameters => new TaskReportQuery(parameters)),
        RepoCommandRegistration.Create(provider, ReportFailure, parameters => new TaskReportFailure(parameters)),
        RepoCommandRegistration.Create(provider, Delay, parameters => new TaskDelay(parameters)),
        RepoCommandRegistration.Create(provider, Delete, parameters => new TaskDelete(parameters)),
        RepoCommandRegistration.Create(provider, Malformed, parameters => new TaskMalformed(parameters)),
        RepoCommandRegistration.Create(provider, Outbox, parameters => new TaskOutboxWrite(parameters)),
    ];
}
