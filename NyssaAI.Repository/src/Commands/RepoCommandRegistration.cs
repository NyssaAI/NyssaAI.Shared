
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
    /// <returns>The registration. Invalid inputs throw argument exceptions.</returns>
    public static RepoCommandRegistration Create<TParameters, TResult>(string databaseProvider,
        RepoCommandKey<TParameters, TResult> key, Func<TParameters, IRepoCommand<TParameters, TResult>> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseProvider);
        return CreateDefault(key, create).ForProvider(databaseProvider.Trim().ToLowerInvariant());
    }

    internal static RepoCommandRegistration CreateDefault<TParameters, TResult>(
        RepoCommandKey<TParameters, TResult> key, Func<TParameters, IRepoCommand<TParameters, TResult>> create)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.Name);
        ArgumentNullException.ThrowIfNull(create);
        return new RepoCommandRegistration(string.Empty, key!.Name, create!);
    }

    internal RepoCommandRegistration ForProvider(string provider) =>
        DatabaseProvider.Length == 0 ? new(provider, Name, Factory) : this;
}
