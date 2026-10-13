using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NyssaAI.Results;

namespace NyssaAI.Repository.Migrations;

/// <summary>Validated immutable snapshot of one provider's compiled steps and exact release pins.</summary>
public sealed partial record MigrationCatalog : IMigrations
{
    private readonly MigrationDefinition[] _definitions;
    private readonly Dictionary<string, long> _releases;

    private MigrationCatalog(string databaseProvider, MigrationDefinition[] definitions, Dictionary<string, long> releases)
    {
        DatabaseProvider = databaseProvider;
        _definitions = definitions;
        _releases = releases;
    }

    /// <summary>Gets the canonical built-in provider identity.</summary>
    public string DatabaseProvider { get; }

    internal IReadOnlyList<MigrationDefinition> Definitions => _definitions;

    /// <summary>Validates and snapshots compiled definitions and pins without database access.</summary>
    /// <param name="databaseProvider">SQLite or PostgreSQL; other providers lack verified transactional migration semantics.</param>
    /// <param name="definitions">Contiguous positive steps starting at one, in any order.</param>
    /// <param name="releases">Unique valid full SemVer identities pointing to available steps or zero.</param>
    public static Result<MigrationCatalog> Create(string databaseProvider, IReadOnlyList<MigrationDefinition> definitions, IReadOnlyList<MigrationReleasePin> releases)
    {
        var provider = databaseProvider?.Trim().ToLowerInvariant();
        if (provider is not ("sqlite" or "postgresql") || definitions is null || releases is null || releases.Count == 0)
            return Invalid("Migration registration requires a supported provider, definitions, and release pins.");
        if (definitions.Any(item => item is null) || releases.Any(item => item is null))
            return Invalid("Migration definitions and pins cannot contain null.");
        var snapshot = definitions.OrderBy(item => item.Number).ToArray();
        for (var index = 0; index < snapshot.Length; index++)
        {
            var item = snapshot[index];
            if (item.Number != index + 1L || !MigrationSql.IsTransactional(item.UpSql) ||
                (item.DownSql is not null && !MigrationSql.IsTransactional(item.DownSql)))
                return Invalid("Migration steps must be contiguous from one and contain transactional SQL.");
        }
        var pins = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var pin in releases)
        {
            if (!IsRelease(pin.Release) || pin.Number < 0 || pin.Number > snapshot.Length || !pins.TryAdd(pin.Release, pin.Number))
                return Invalid("Migration pins require unique full semantic versions and available schema numbers.");
        }
        return Result<MigrationCatalog>.Success(new(provider, snapshot, pins));
    }

    Result<long> IMigrations.Resolve(string release)
    {
        if (!IsRelease(release)) return Result<long>.Failure(Error.FromMessage("The requested release is not a semantic version.", ErrorCategory.Validation));
        return _releases.TryGetValue(release, out var number)
            ? Result<long>.Success(number)
            : Result<long>.Failure(Error.FromMessage("No exact migration pin exists for the requested release.", ErrorCategory.NotFound));
    }

    internal static string Fingerprint(MigrationDefinition definition)
    {
        var content = definition.Number.ToString(CultureInfo.InvariantCulture) + ":" + definition.UpSql.Length.ToString(CultureInfo.InvariantCulture) + ":" + definition.UpSql + ":" + definition.DownSql;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    private static bool IsRelease(string? release) => release is not null && ReleasePattern().IsMatch(release);
    private static Result<MigrationCatalog> Invalid(string message) => Result<MigrationCatalog>.Failure(Error.FromMessage(message, ErrorCategory.Validation));

    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex ReleasePattern();
}
