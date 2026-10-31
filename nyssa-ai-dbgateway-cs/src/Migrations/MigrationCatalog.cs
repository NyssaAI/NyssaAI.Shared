using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NyssaAI.DbGateway.Migrations;

/// <summary>Validated immutable snapshot of paired/universal SQLite and PostgreSQL migration steps and release pins.</summary>
public sealed partial record MigrationCatalog
{
    private readonly MigrationStep[] _steps;
    private readonly Dictionary<string, long> _releases;

    private MigrationCatalog(MigrationStep[] steps, Dictionary<string, long> releases, int commandTimeoutSeconds)
    {
        _steps = steps;
        _releases = releases;
        CommandTimeoutSeconds = commandTimeoutSeconds;
    }

    /// <summary>Gets the default command timeout in seconds for migration operations (0 for infinite).</summary>
    public int CommandTimeoutSeconds { get; }

    internal IReadOnlyList<MigrationStep> Steps => _steps;

    /// <summary>Validates and snapshots compiled migration steps and release pins without database access.</summary>
    /// <param name="steps">Contiguous positive steps starting at one, in any order.</param>
    /// <param name="releases">Unique valid full SemVer identities pointing to available steps or zero.</param>
    /// <param name="commandTimeoutSeconds">Default command timeout in seconds (0 for infinite; defaults to 30).</param>
    /// <returns>The validated immutable catalog.</returns>
    /// <exception cref="ArgumentException">Steps, arguments, timeouts, or release pins are invalid.</exception>
    public static MigrationCatalog Create(
        IReadOnlyList<MigrationStep> steps,
        IReadOnlyList<MigrationReleasePin> releases,
        int commandTimeoutSeconds = 30)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentOutOfRangeException.ThrowIfNegative(commandTimeoutSeconds);
        if (releases.Count == 0)
            throw new ArgumentException("Migration registration requires release pins.", nameof(releases));
        if (releases.Any(item => item is null))
            throw new ArgumentException("Migration pins cannot contain null.", nameof(releases));
        if (steps.Any(item => item is null))
            throw new ArgumentException("Migration steps cannot contain null.", nameof(steps));

        var ordered = steps.OrderBy(item => item.Number).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var step = ordered[index];
            if (step.Number != index + 1)
                throw new ArgumentException("Migration steps must be contiguous starting at 1.", nameof(steps));
            if (step.CommandTimeoutSeconds is < 0)
                throw new ArgumentException("Migration step timeouts cannot be negative.", nameof(steps));
            if (!MigrationSql.IsTransactional(step.SqliteUpSql) ||
                !MigrationSql.IsTransactional(step.PostgresUpSql) ||
                (step.SqliteDownSql is not null && !MigrationSql.IsTransactional(step.SqliteDownSql)) ||
                (step.PostgresDownSql is not null && !MigrationSql.IsTransactional(step.PostgresDownSql)))
            {
                throw new ArgumentException("Migration SQL must contain valid transactional statements.", nameof(steps));
            }
        }

        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var pin in releases)
        {
            if (string.IsNullOrWhiteSpace(pin.Release) || !SemVerPattern().IsMatch(pin.Release))
                throw new ArgumentException("Migration release pins must be exact full SemVer strings.", nameof(releases));
            if (pin.Number < 0 || pin.Number > ordered.Length)
                throw new ArgumentException("Migration release pins must target an existing step or 0.", nameof(releases));
            if (!map.TryAdd(pin.Release, pin.Number))
                throw new ArgumentException("Migration release pins must be unique.", nameof(releases));
        }
        return new MigrationCatalog(ordered, map, commandTimeoutSeconds);
    }

    internal long Resolve(string release)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(release);
        if (!_releases.TryGetValue(release, out var target))
            throw new KeyNotFoundException("The requested migration release is not registered.");
        return target;
    }

    internal static string Fingerprint(MigrationStep step, DbDialect dialect)
    {
        var upSql = step.GetUpSql(dialect);
        var downSql = step.GetDownSql(dialect);
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{step.Number}:{upSql.Length}:{upSql}:{downSql?.Length ?? -1}:{downSql}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$")]
    private static partial Regex SemVerPattern();
}
