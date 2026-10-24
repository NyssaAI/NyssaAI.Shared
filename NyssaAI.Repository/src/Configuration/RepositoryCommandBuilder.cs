using System.Runtime.CompilerServices;
using NyssaAI.Repository.Commands;

namespace NyssaAI.Repository.Configuration;

/// <summary>Registers typed command factories during application startup.</summary>
/// <remarks>Omitted providers use the configured database provider. Factories run only when commands are selected.
/// Throw from a factory to reject parameters. The callback's registrations are snapshotted when it returns.</remarks>
public sealed class RepositoryCommandBuilder
{
    private readonly List<RepoCommandRegistration> _registrations = [];
    internal RepositoryCommandBuilder() { }

    /// <summary>Adds a command factory for the configured database provider.</summary>
    /// <typeparam name="TParameters">The input type fixed by the key.</typeparam>
    /// <typeparam name="TResult">The complete result type fixed by the key.</typeparam>
    /// <param name="key">The logical operation.</param>
    /// <param name="create">Creates a fresh command for the supplied parameters, or throws.</param>
    /// <returns>This builder for chaining registrations.</returns>
    [OverloadResolutionPriority(1)]
    public RepositoryCommandBuilder Add<TParameters, TResult>(RepoCommandKey<TParameters, TResult> key,
        Func<TParameters, IRepoCommand<TParameters, TResult>> create)
    {
        _registrations.Add(RepoCommandRegistration.CreateDefault(key, create));
        return this;
    }

    /// <summary>Adds a command delegate for the configured database provider without requiring a command class.</summary>
    /// <typeparam name="TParameters">The input type fixed by the key.</typeparam>
    /// <typeparam name="TResult">The complete result type fixed by the key.</typeparam>
    /// <param name="key">The logical operation.</param>
    /// <param name="execute">Executes the command using the supplied parameters and borrowed scope resources.</param>
    /// <returns>This builder for chaining registrations.</returns>
    public RepositoryCommandBuilder Add<TParameters, TResult>(RepoCommandKey<TParameters, TResult> key,
        RepoCommandHandler<TParameters, TResult> execute)
    {
        _registrations.Add(RepoCommandRegistration.CreateDefault(key, execute));
        return this;
    }

    /// <summary>Adds a command factory for an explicitly named provider.</summary>
    /// <typeparam name="TParameters">The input type fixed by the key.</typeparam>
    /// <typeparam name="TResult">The complete result type fixed by the key.</typeparam>
    /// <param name="databaseProvider">The provider name, normalized during registration.</param>
    /// <param name="key">The logical operation.</param>
    /// <param name="create">Creates a fresh command for the supplied parameters, or throws.</param>
    /// <returns>This builder for chaining registrations.</returns>
    [OverloadResolutionPriority(1)]
    public RepositoryCommandBuilder Add<TParameters, TResult>(string databaseProvider,
        RepoCommandKey<TParameters, TResult> key, Func<TParameters, IRepoCommand<TParameters, TResult>> create)
    {
        _registrations.Add(RepoCommandRegistration.Create(databaseProvider, key, create));
        return this;
    }

    /// <summary>Adds a command delegate for an explicitly named provider without requiring a command class.</summary>
    /// <typeparam name="TParameters">The input type fixed by the key.</typeparam>
    /// <typeparam name="TResult">The complete result type fixed by the key.</typeparam>
    /// <param name="databaseProvider">The provider name, normalized during registration.</param>
    /// <param name="key">The logical operation.</param>
    /// <param name="execute">Executes the command using the supplied parameters and borrowed scope resources.</param>
    /// <returns>This builder for chaining registrations.</returns>
    public RepositoryCommandBuilder Add<TParameters, TResult>(string databaseProvider,
        RepoCommandKey<TParameters, TResult> key, RepoCommandHandler<TParameters, TResult> execute)
    {
        _registrations.Add(RepoCommandRegistration.Create(databaseProvider, key, execute));
        return this;
    }

    internal RepoCommandRegistration[] Snapshot() => _registrations.ToArray();
}
