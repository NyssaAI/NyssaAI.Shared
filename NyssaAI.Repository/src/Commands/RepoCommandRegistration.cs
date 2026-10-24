
using System.Runtime.CompilerServices;

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
    [OverloadResolutionPriority(1)]
    public static RepoCommandRegistration Create<TParameters, TResult>(string databaseProvider,
        RepoCommandKey<TParameters, TResult> key, Func<TParameters, IRepoCommand<TParameters, TResult>> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseProvider);
        return CreateDefault(key, create).ForProvider(databaseProvider.Trim().ToLowerInvariant());
    }

    /// <summary>Creates a typed delegate registration without requiring a custom command class.</summary>
    /// <typeparam name="TParameters">The input contract.</typeparam>
    /// <typeparam name="TResult">The complete successful result contract.</typeparam>
    /// <param name="databaseProvider">The matching provider identity.</param>
    /// <param name="key">The typed logical operation.</param>
    /// <param name="execute">Executes the command using the supplied parameters and borrowed scope resources.</param>
    /// <returns>The registration. Invalid inputs throw argument exceptions.</returns>
    public static RepoCommandRegistration Create<TParameters, TResult>(string databaseProvider,
        RepoCommandKey<TParameters, TResult> key, RepoCommandHandler<TParameters, TResult> execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseProvider);
        return CreateDefault(key, execute).ForProvider(databaseProvider.Trim().ToLowerInvariant());
    }

    internal static RepoCommandRegistration CreateDefault<TParameters, TResult>(
        RepoCommandKey<TParameters, TResult> key, Func<TParameters, IRepoCommand<TParameters, TResult>> create)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.Name);
        ArgumentNullException.ThrowIfNull(create);
        return new RepoCommandRegistration(string.Empty, key!.Name, create!);
    }

    internal static RepoCommandRegistration CreateDefault<TParameters, TResult>(
        RepoCommandKey<TParameters, TResult> key, RepoCommandHandler<TParameters, TResult> execute)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.Name);
        ArgumentNullException.ThrowIfNull(execute);
        Func<TParameters, IRepoCommand<TParameters, TResult>> create =
            parameters => new RepoCommandDelegate<TParameters, TResult>(key, parameters, execute);
        return new RepoCommandRegistration(string.Empty, key.Name, create);
    }

    internal RepoCommandRegistration ForProvider(string provider) =>
        DatabaseProvider.Length == 0 ? new(provider, Name, Factory) : this;
}
