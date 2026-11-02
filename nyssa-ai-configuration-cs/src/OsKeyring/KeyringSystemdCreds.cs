using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.OsKeyring;

/// <summary>
/// Reads credentials provisioned into the process environment by <c>systemd</c> via
/// <c>CREDENTIALS_DIRECTORY</c> (<c>LoadCredential=</c> / <c>LoadCredentialEncrypted=</c> /
/// <c>systemd-creds</c>).
/// </summary>
internal sealed class KeyringSystemdCreds : IKeyring
{
    /// <summary>The environment variable set by systemd when service credentials are mounted.</summary>
    public const string CredentialsDirectoryVariable = "CREDENTIALS_DIRECTORY";

    private const string ImmutableMessage =
        "systemd-creds is immutable at runtime; credentials must be provisioned via systemd service configuration.";

    private readonly string? _systemdCredentialName;

    /// <summary>
    /// Creates a keyring backed by <c>CREDENTIALS_DIRECTORY</c>.
    /// </summary>
    /// <param name="systemdCredentialName">Optional explicit credential filename to look up first.</param>
    public KeyringSystemdCreds(string? systemdCredentialName = null)
    {
        _systemdCredentialName = systemdCredentialName;
    }

    /// <summary>
    /// Returns true when <c>CREDENTIALS_DIRECTORY</c> is set and points to an existing directory.
    /// </summary>
    public static bool IsAvailable()
    {
        var directory = Environment.GetEnvironmentVariable(CredentialsDirectoryVariable);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
    }

    /// <inheritdoc/>
    public Result<string?> Get(string service, string key)
    {
        var target = KeyringAddress.Compose(service, key);
        if (target.IsFailure)
            return Result<string?>.Failure(target.Errors);

        var directory = Environment.GetEnvironmentVariable(CredentialsDirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return Result<string?>.Failure(DirectoryUnavailable);

        try
        {
            var credName = KeyringLinux.GetCredentialName(service, key);
            var found = TryReadCredentialFile(directory, _systemdCredentialName, credName, service, key);
            return found is not null
                ? Result<string?>.Success(found)
                : Result<string?>.Failure(CredentialNotFound(service, key));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<string?>.Failure(ReadFailed(exception));
        }
    }

    /// <inheritdoc/>
    public Result Set(string service, string key, string secret)
    {
        var target = KeyringAddress.Compose(service, key);
        return target.IsFailure
            ? Result.Failure(target.Errors)
            : Result.Failure(ImmutableStore);
    }

    /// <inheritdoc/>
    public Result Delete(string service, string key)
    {
        var target = KeyringAddress.Compose(service, key);
        return target.IsFailure
            ? Result.Failure(target.Errors)
            : Result.Failure(ImmutableStore);
    }

    internal static string? TryReadCredentialFile(
        string directory,
        string? explicitName,
        string credName,
        string service,
        string key)
    {
        string?[] candidates =
        [
            explicitName,
            credName,
            $"{service}.key",
            $"{key}.key",
            service,
            key,
            "nyssa-key",
            "master-key",
        ];

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var path = Path.Combine(directory, candidate);
            if (!File.Exists(path))
                continue;

            var value = File.ReadAllText(path).Trim();
            if (value.Length > 0)
                return value;
        }

        var files = Directory.GetFiles(directory);
        if (files.Length == 1)
        {
            var value = File.ReadAllText(files[0]).Trim();
            if (value.Length > 0)
                return value;
        }

        return null;
    }

    /// <summary>The failure returned when Set or Delete is called on an immutable systemd credential directory.</summary>
    public static Error ImmutableStore { get; } = Error.FromMessages(
        ImmutableMessage,
        $"keyring: {ImmutableMessage}",
        ErrorCategory.Forbidden);

    /// <summary>The failure returned when CREDENTIALS_DIRECTORY is not set or does not exist.</summary>
    public static Error DirectoryUnavailable { get; } = Error.FromMessages(
        "The credential store could not be read.",
        "keyring: CREDENTIALS_DIRECTORY is not set or does not point to an existing directory",
        ErrorCategory.Unavailable);

    /// <summary>The failure returned when no matching credential file exists in CREDENTIALS_DIRECTORY.</summary>
    /// <param name="service">The service name.</param>
    /// <param name="key">The key name.</param>
    public static Error CredentialNotFound(string service, string key) => Error.FromMessages(
        "The credential could not be found.",
        $"keyring: no credential matching '{service}/{key}' was found in CREDENTIALS_DIRECTORY",
        ErrorCategory.NotFound);

    /// <summary>The failure returned when reading a credential file in CREDENTIALS_DIRECTORY fails.</summary>
    /// <param name="exception">The I/O failure.</param>
    public static Error ReadFailed(Exception exception) => Error.FromMessages(
        "The credential store could not be read.",
        $"keyring: {exception.GetType().Name}: {exception.Message}",
        ErrorCategory.Unavailable);
}
