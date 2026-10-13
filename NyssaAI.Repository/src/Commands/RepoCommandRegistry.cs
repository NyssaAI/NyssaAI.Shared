using NyssaAI.Results;

namespace NyssaAI.Repository.Commands;

internal sealed class RepoCommandRegistry : ICommands
{
    private readonly string _databaseProvider;
    private readonly IReadOnlyList<RepoCommandRegistration> _registrations;

    internal RepoCommandRegistry(string databaseProvider, IReadOnlyList<RepoCommandRegistration> registrations)
    {
        _databaseProvider = databaseProvider;
        _registrations = registrations;
    }

    public Result<IRepoCommand<TParameters, TResult>> Resolve<TParameters, TResult>(RepoCommandKey<TParameters, TResult> key, TParameters parameters)
    {
        if (key is null || string.IsNullOrWhiteSpace(key.Name) || parameters is null)
            return Fail<TParameters, TResult>("A command key and parameters are required.", ErrorCategory.Validation);
        var matches = _registrations.Where(registration =>
            string.Equals(registration.DatabaseProvider, _databaseProvider, StringComparison.Ordinal) &&
            string.Equals(registration.Name, key.Name, StringComparison.Ordinal)).ToArray();
        if (matches.Length == 0)
            return Fail<TParameters, TResult>("No command is registered for this operation and provider.", ErrorCategory.NotFound);
        if (matches.Length != 1)
            return Fail<TParameters, TResult>("Multiple commands are registered for this operation and provider.", ErrorCategory.Conflict);
        if (matches[0].Factory is not Func<TParameters, Result<IRepoCommand<TParameters, TResult>>> create)
            return Fail<TParameters, TResult>("The registered command has incompatible input or result contracts.", ErrorCategory.Validation);
        var created = create(parameters);
        if (created is null)
            return Fail<TParameters, TResult>("The command factory returned no outcome.", ErrorCategory.Validation);
        if (created.IsFailure) return created;
        if (created.Value is null || created.Value.Key != key || !EqualityComparer<TParameters>.Default.Equals(created.Value.Parameters, parameters))
            return Fail<TParameters, TResult>("The command factory changed the operation or supplied parameters.", ErrorCategory.Validation);
        return Result<IRepoCommand<TParameters, TResult>>.Success(new RepoCommandResolved<TParameters, TResult>(_databaseProvider, created.Value));
    }

    private static Result<IRepoCommand<TParameters, TResult>> Fail<TParameters, TResult>(string message, ErrorCategory category) =>
        Result<IRepoCommand<TParameters, TResult>>.Failure(Error.FromMessage(message, category));
}
