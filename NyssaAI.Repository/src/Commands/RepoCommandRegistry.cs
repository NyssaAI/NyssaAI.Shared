
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

    public IRepoCommand<TParameters, TResult> Resolve<TParameters, TResult>(RepoCommandKey<TParameters, TResult> key, TParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.Name);
        ArgumentNullException.ThrowIfNull(parameters);
        var matches = _registrations.Where(registration =>
            string.Equals(registration.DatabaseProvider, _databaseProvider, StringComparison.Ordinal) &&
            string.Equals(registration.Name, key.Name, StringComparison.Ordinal)).ToArray();
        if (matches.Length == 0)
            throw new KeyNotFoundException("No command is registered for this operation and provider.");
        if (matches.Length != 1)
            throw new InvalidOperationException("Multiple commands are registered for this operation and provider.");
        if (matches[0].Factory is not Func<TParameters, IRepoCommand<TParameters, TResult>> create)
            throw new InvalidOperationException("The registered command has incompatible input or result contracts.");
        var created = create(parameters);
        if (created is null)
            throw new InvalidOperationException("The command factory returned no command.");
        if (created.Key != key || !EqualityComparer<TParameters>.Default.Equals(created.Parameters, parameters))
            throw new InvalidOperationException("The command factory changed the operation or supplied parameters.");
        return new RepoCommandResolved<TParameters, TResult>(_databaseProvider, created);
    }

}
