using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NyssaAI.Repository.Migrations;

/// <summary>Validated immutable snapshot of one provider's compiled steps and exact release pins.</summary>
public sealed partial record MigrationCatalog : IMigrations
{
    private readonly MigrationDefinition[] _definitions;
    private readonly Dictionary<string, long> _releases;

    private MigrationCatalog(string databaseProvider, MigrationDefinition[] definitions, Dictionary<string, long> releases, int commandTimeoutSeconds)
    {
        DatabaseProvider = databaseProvider;
        _definitions = definitions;
        _releases = releases;
        CommandTimeoutSeconds = commandTimeoutSeconds;
    }

    /// <summary>Gets the canonical built-in provider identity.</summary>
    public string DatabaseProvider { get; }

    /// <summary>Gets the default command timeout in seconds for migration operations (0 for infinite).</summary>
    public int CommandTimeoutSeconds { get; }

    internal IReadOnlyList<MigrationDefinition> Definitions => _definitions;

    /// <summary>Validates and snapshots compiled definitions and pins without database access.</summary>
    /// <param name="databaseProvider">SQLite or PostgreSQL; other providers lack verified transactional migration semantics.</param>
    /// <param name="definitions">Contiguous positive steps starting at one, in any order.</param>
    /// <param name="releases">Unique valid full SemVer identities pointing to available steps or zero.</param>
    /// <param name="commandTimeoutSeconds">Default command timeout in seconds (0 for infinite; defaults to 30).</param>
    /// <returns>The validated immutable catalog directly.</returns>
    /// <exception cref="ArgumentException">Definitions, arguments, timeouts, or release pins are invalid.</exception>
    /// <exception cref="NotSupportedException">The named provider has no verified migration support.</exception>
    public static MigrationCatalog Create(string databaseProvider, IReadOnlyList<MigrationDefinition> definitions, IReadOnlyList<MigrationReleasePin> releases, int commandTimeoutSeconds = 30)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseProvider);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentOutOfRangeException.ThrowIfNegative(commandTimeoutSeconds);
        var provider = databaseProvider?.Trim().ToLowerInvariant();
        if (provider is not ("sqlite" or "postgresql"))
            throw new NotSupportedException("This provider does not support verified transactional migrations.");
        if (releases.Count == 0)
            throw new ArgumentException("Migration registration requires release pins.", nameof(releases));
        if (releases.Any(item => item is null))
            throw new ArgumentException("Migration pins cannot contain null.", nameof(releases));
        if (definitions.Any(item => item is null))
            throw new ArgumentException("Migration definitions and pins cannot contain null.", nameof(definitions));
        var snapshot = definitions.OrderBy(item => item.Number).ToArray();
        for (var index = 0; index < snapshot.Length; index++)
        {
            var item = snapshot[index];
            if (item.CommandTimeoutSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(definitions), "Migration command timeout cannot be negative.");
            if (item.Number != index + 1L || !MigrationSql.IsTransactional(item.UpSql) ||
                (item.DownSql is not null && !MigrationSql.IsTransactional(item.DownSql)))
                throw new ArgumentException("Migration steps must be contiguous from one and contain transactional SQL.", nameof(definitions));
        }
        var pins = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var pin in releases)
        {
            if (!IsRelease(pin.Release) || pin.Number < 0 || pin.Number > snapshot.Length || !pins.TryAdd(pin.Release, pin.Number))
                throw new ArgumentException("Migration pins require unique full semantic versions and available schema numbers.", nameof(releases));
        }
        return new(provider, snapshot, pins, commandTimeoutSeconds);
    }

    long IMigrations.Resolve(string release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (!IsRelease(release)) throw new ArgumentException("The requested release is not a semantic version.", nameof(release));
        return _releases.TryGetValue(release, out var number)
            ? number
            : throw new KeyNotFoundException("No exact migration pin exists for the requested release.");
    }

    internal static string Fingerprint(MigrationDefinition definition)
    {
        var content = definition.Number.ToString(CultureInfo.InvariantCulture) + ":" + definition.UpSql.Length.ToString(CultureInfo.InvariantCulture) + ":" + definition.UpSql + ":" + definition.DownSql;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    private static bool IsRelease(string? release) => release is not null && ReleasePattern().IsMatch(release);

    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex ReleasePattern();
}
