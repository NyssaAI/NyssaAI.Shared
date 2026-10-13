using NyssaAI.Results;

namespace NyssaAI.Repository.Commands;

/// <summary>RepoCommandRegistration describes an explicitly compiled command factory for one provider and typed operation.</summary>
public sealed record RepoCommandRegistration
{
    private RepoCommandRegistration(string databaseProvider, string name, object factory)
    {
        DatabaseProvider = databaseProvider;
        Name = name;
        Factory = factory;
    }

    /// <summary>Gets the canonical provider identity.</summary>
    public string DatabaseProvider { get; }

    /// <summary>Gets the case-sensitive logical operation name.</summary>
    public string Name { get; }

    internal object Factory { get; }

    /// <summary>Creates a typed registration without reflection, database access, or invoking its factory.</summary>
    /// <typeparam name="TParameters">The input contract.</typeparam>
    /// <typeparam name="TResult">The complete successful result contract.</typeparam>
    /// <param name="databaseProvider">The matching provider identity.</param>
    /// <param name="key">The typed logical operation.</param>
    /// <param name="create">Validates values and creates a fresh command for each resolution.</param>
    /// <returns>A registration, or validation failure for missing inputs.</returns>
    public static Result<RepoCommandRegistration> Create<TParameters, TResult>(string databaseProvider,
        RepoCommandKey<TParameters, TResult> key, Func<TParameters, Result<IRepoCommand<TParameters, TResult>>> create)
    {
        if (string.IsNullOrWhiteSpace(databaseProvider) || key is null || string.IsNullOrWhiteSpace(key.Name) || create is null)
            return Result<RepoCommandRegistration>.Failure(Error.FromMessage("Command registration requires a provider, key, and factory.", ErrorCategory.Validation));
        return Result<RepoCommandRegistration>.Success(new(databaseProvider.Trim().ToLowerInvariant(), key.Name, create));
    }
}
