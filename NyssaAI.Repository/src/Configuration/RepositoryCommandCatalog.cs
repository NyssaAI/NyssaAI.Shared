using NyssaAI.Repository.Commands;

namespace NyssaAI.Repository.Configuration;

// Each registration call replaces this snapshot. Already-built containers keep their original catalog.
internal sealed record RepositoryCommandCatalog
{
    internal string? DatabaseProvider { get; init; }
    internal required IReadOnlyList<RepoCommandRegistration> Registrations { get; init; }

    internal RepositoryCommandCatalog Append(IReadOnlyList<RepoCommandRegistration> additions, string? provider = null)
    {
        var selected = provider ?? DatabaseProvider;
        var entries = Registrations.Concat(additions).ToArray();
        if (entries.Any(entry => entry is null))
            throw new ArgumentException("Command registrations cannot contain null.", nameof(additions));
        var identities = entries.Select(entry => (Provider: entry.DatabaseProvider.Length == 0 ? selected ?? string.Empty : entry.DatabaseProvider, entry.Name));
        if (identities.Distinct().Count() != entries.Length)
            throw new InvalidOperationException("Command registrations require unique operation names for each provider.");
        return new RepositoryCommandCatalog { DatabaseProvider = selected, Registrations = entries };
    }

    internal RepoCommandRegistration[] Resolve() => Registrations.Select(entry => entry.ForProvider(DatabaseProvider!)).ToArray();
}
