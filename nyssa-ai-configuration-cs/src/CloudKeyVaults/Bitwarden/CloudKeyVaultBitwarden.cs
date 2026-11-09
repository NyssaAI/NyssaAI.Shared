using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.CloudKeyVaults.Bitwarden;

/// <summary>
/// <see cref="ICloudKeyVault"/> implementation backed by the Bitwarden Secrets Manager CLI (<c>bws</c>).
/// </summary>
public sealed class CloudKeyVaultBitwarden : ICloudKeyVault
{
    /// <summary>The default CLI binary invoked when no override path is configured.</summary>
    public const string DefaultBinary = "bws";

    /// <summary>The environment variable carrying the Bitwarden machine account access token.</summary>
    public const string AccessTokenEnvironmentVariable = "BWS_ACCESS_TOKEN";

    /// <summary>The environment variable carrying the Bitwarden organization identifier.</summary>
    public const string OrganizationIdEnvironmentVariable = "BWS_ORGANIZATION_ID";

    /// <summary>The environment variable carrying an override Bitwarden server URL.</summary>
    public const string ServerUrlEnvironmentVariable = "BWS_SERVER_URL";

    private readonly string _name;
    private readonly string? _projectId;
    private readonly string? _accessToken;
    private readonly IAccessTokenSource? _tokens;
    private readonly string? _organizationId;
    private readonly string? _serverUrl;
    private readonly string _binaryPath;
    private readonly Func<string, IReadOnlyList<string>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<Result<string>>> _runner;
    private Action<string, string>? _onSecretListed;

    /// <summary>
    /// Creates a Bitwarden Secrets Manager vault adapter from <paramref name="settings"/> and an optional <paramref name="tokens"/> source.
    /// </summary>
    /// <param name="settings">The vault manifest or bootstrap configuration.</param>
    /// <param name="tokens">Optional OAuth/credential bearer token source.</param>
    /// <param name="runner">Optional process execution delegate for deterministic testing.</param>
    public CloudKeyVaultBitwarden(
        VaultOptions settings,
        IAccessTokenSource? tokens = null,
        Func<string, IReadOnlyList<string>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<Result<string>>>? runner = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _name = settings.Name;
        _tokens = tokens;
        _projectId = TryGetSetting(settings.Settings, "projectId");
        _organizationId = TryGetSetting(settings.Settings, "organizationId");
        _serverUrl = settings.Endpoint?.AbsoluteUri
            ?? TryGetSetting(settings.Settings, "serverUrl")
            ?? TryGetSetting(settings.Settings, "apiUrl");
        _binaryPath = TryGetSetting(settings.Settings, "cliPath")
            ?? TryGetSetting(settings.Settings, "bwsCliPath")
            ?? DefaultBinary;
        _accessToken = TryGetSetting(settings.Secrets, "accessToken")
            ?? TryGetSetting(settings.Secrets, "machineAccessToken");
        _runner = runner ?? RunProcessAsync;
    }

    /// <summary>Gets the configured provider name for this vault instance.</summary>
    public string Name => _name;

    /// <summary>Registers an optional callback invoked with <c>(pointerKey, secretValue)</c> whenever <c>bws secret list</c> returns inline secret values.</summary>
    /// <param name="callback">The callback to notify for each listed secret with a non-null string value.</param>
    public void SetSecretListedCallback(Action<string, string>? callback) => _onSecretListed = callback;

    /// <inheritdoc/>
    public async ValueTask<Result<string?>> ReadAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        cancellationToken.ThrowIfCancellationRequested();

        var env = await ResolveEnvironmentAsync(cancellationToken).ConfigureAwait(false);
        if (env.IsFailure)
            return Result<string?>.Failure(env.Errors);

        var list = await ListSecretsAsync(env.Value, cancellationToken).ConfigureAwait(false);
        if (list.IsFailure)
            return Result<string?>.Failure(list.Errors);

        NotifyListedSecrets(list.Value);
        var target = FindMatchingItem(list.Value, pointer);
        if (target is null || string.IsNullOrWhiteSpace(target.Id))
            return Result<string?>.Success(null);

        if (target.Value is not null)
            return Result<string?>.Success(target.Value);

        var output = await _runner(
            _binaryPath,
            ["secret", "get", target.Id, "--output", "json"],
            env.Value,
            cancellationToken).ConfigureAwait(false);

        if (output.IsFailure)
            return Result<string?>.Failure(output.Errors);

        try
        {
            var detail = JsonSerializer.Deserialize(output.Value, BwsJsonContext.Default.BwsSecretEntry);
            return detail is null
                ? Result<string?>.Failure(InvalidPayload(_name, "empty secret detail response"))
                : Result<string?>.Success(detail.Value);
        }
        catch (JsonException exception)
        {
            return Result<string?>.Failure(InvalidPayload(_name, exception.Message));
        }
    }

    /// <inheritdoc/>
    public async ValueTask<Result<ConfigurationPointer>> WriteAsync(
        string @namespace,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pointer = ConfigurationPointer.Parse(_name, $"{@namespace}/{key}");
        if (pointer.IsFailure)
            return pointer;

        var env = await ResolveEnvironmentAsync(cancellationToken).ConfigureAwait(false);
        if (env.IsFailure)
            return Result<ConfigurationPointer>.Failure(env.Errors);

        var list = await ListSecretsAsync(env.Value, cancellationToken).ConfigureAwait(false);
        if (list.IsFailure)
            return Result<ConfigurationPointer>.Failure(list.Errors);

        var existing = FindMatchingItem(list.Value, pointer.Value);
        IReadOnlyList<string> arguments;
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.Id))
        {
            arguments = ["secret", "edit", existing.Id, "--value", value, "--output", "json"];
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_projectId))
                return Result<ConfigurationPointer>.Failure(ProjectIdRequired(_name));

            arguments = ["secret", "create", pointer.Value.Value, value, _projectId, "--output", "json"];
        }

        var written = await _runner(_binaryPath, arguments, env.Value, cancellationToken).ConfigureAwait(false);
        return written.IsFailure
            ? Result<ConfigurationPointer>.Failure(written.Errors)
            : Result<ConfigurationPointer>.Success(pointer.Value);
    }

    /// <inheritdoc/>
    public async ValueTask<Result<ConfigurationRemoval>> RemoveAsync(
        ConfigurationPointer pointer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        cancellationToken.ThrowIfCancellationRequested();

        var env = await ResolveEnvironmentAsync(cancellationToken).ConfigureAwait(false);
        if (env.IsFailure)
            return Result<ConfigurationRemoval>.Failure(env.Errors);

        var list = await ListSecretsAsync(env.Value, cancellationToken).ConfigureAwait(false);
        if (list.IsFailure)
            return Result<ConfigurationRemoval>.Failure(list.Errors);

        var existing = FindMatchingItem(list.Value, pointer);
        if (existing is null || string.IsNullOrWhiteSpace(existing.Id))
            return Result<ConfigurationRemoval>.Success(new ConfigurationRemoval { RemoteValueRemains = false });

        var deleted = await _runner(
            _binaryPath,
            ["secret", "delete", existing.Id],
            env.Value,
            cancellationToken).ConfigureAwait(false);

        return deleted.IsFailure
            ? Result<ConfigurationRemoval>.Failure(deleted.Errors)
            : Result<ConfigurationRemoval>.Success(new ConfigurationRemoval { RemoteValueRemains = false });
    }

    /// <summary>The failure returned when creating a secret without a configured Bitwarden projectId.</summary>
    /// <param name="vault">The vault provider name.</param>
    public static Error ProjectIdRequired(string vault) => Error.FromMessages(
        "The configured vault is missing a required setting.",
        $"vault: '{vault}' requires settings['projectId'] to create secrets in Bitwarden Secrets Manager",
        ErrorCategory.Validation);

    /// <summary>The failure returned when no access token or credential is configured for the vault.</summary>
    /// <param name="vault">The vault provider name.</param>
    public static Error AccessTokenRequired(string vault) => Error.FromMessages(
        "The configured vault is missing authentication credentials.",
        $"vault: '{vault}' requires either a credential address or secrets['accessToken']",
        ErrorCategory.Validation);

    /// <summary>The failure returned when the Bitwarden CLI returns malformed JSON.</summary>
    /// <param name="vault">The vault provider name.</param>
    /// <param name="detail">Safe diagnostic parser detail.</param>
    public static Error InvalidPayload(string vault, string detail) => Error.FromMessages(
        "The secret store returned an unreadable response.",
        $"vault: '{vault}' received invalid JSON from Bitwarden Secrets Manager: {detail}",
        ErrorCategory.Unavailable);

    /// <summary>The failure returned when the Bitwarden CLI binary fails or cannot be started.</summary>
    /// <param name="vault">The vault provider name.</param>
    /// <param name="detail">Safe diagnostic failure detail.</param>
    /// <param name="category">The classified error category.</param>
    public static Error CommandFailed(string vault, string detail, ErrorCategory category = ErrorCategory.Unavailable) =>
        Error.FromMessages(
            "The secret store could not complete the request.",
            $"vault: '{vault}' command failed: {detail}",
            category);

    private void NotifyListedSecrets(List<BwsSecretEntry> items)
    {
        var callback = _onSecretListed;
        if (callback is null)
            return;

        foreach (var item in items)
        {
            if (item.Value is null)
                continue;
            if (!string.IsNullOrWhiteSpace(item.Id))
                callback(item.Id.Trim(), item.Value);
            var resolvedKey = item.ResolvedKey;
            if (!string.IsNullOrWhiteSpace(resolvedKey))
            {
                var trimmed = resolvedKey.Trim();
                callback(trimmed, item.Value);
                var dotIndex = trimmed.IndexOf('.', StringComparison.Ordinal);
                if (dotIndex > 0 && !trimmed.Contains('/', StringComparison.Ordinal))
                    callback(string.Concat(trimmed.AsSpan(0, dotIndex), "/", trimmed.AsSpan(dotIndex + 1)), item.Value);
            }
        }
    }

    private async ValueTask<Result<IReadOnlyDictionary<string, string>>> ResolveEnvironmentAsync(
        CancellationToken cancellationToken)
    {
        string? token = _accessToken;
        if (_tokens is not null)
        {
            var resolved = await _tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (resolved.IsFailure)
                return Result<IReadOnlyDictionary<string, string>>.Failure(resolved.Errors);
            token = resolved.Value;
        }

        if (string.IsNullOrWhiteSpace(token))
            return Result<IReadOnlyDictionary<string, string>>.Failure(AccessTokenRequired(_name));

        var env = new Dictionary<string, string>(StringComparer.Ordinal) { [AccessTokenEnvironmentVariable] = token };
        if (!string.IsNullOrWhiteSpace(_organizationId))
            env[OrganizationIdEnvironmentVariable] = _organizationId;
        if (!string.IsNullOrWhiteSpace(_serverUrl))
            env[ServerUrlEnvironmentVariable] = _serverUrl;

        return Result<IReadOnlyDictionary<string, string>>.Success(env);
    }

    private async ValueTask<Result<List<BwsSecretEntry>>> ListSecretsAsync(
        IReadOnlyDictionary<string, string> env,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> args = string.IsNullOrWhiteSpace(_projectId)
            ? ["secret", "list", "--output", "json"]
            : ["secret", "list", _projectId, "--output", "json"];

        var output = await _runner(_binaryPath, args, env, cancellationToken).ConfigureAwait(false);
        if (output.IsFailure)
        {
            var message = output.Errors[0].TechnicalMessage;
            if (message.Contains("404", StringComparison.Ordinal) && message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return Result<List<BwsSecretEntry>>.Success([]);
            return Result<List<BwsSecretEntry>>.Failure(output.Errors);
        }

        try
        {
            var items = JsonSerializer.Deserialize(output.Value, BwsJsonContext.Default.ListBwsSecretEntry);
            return items is null
                ? Result<List<BwsSecretEntry>>.Failure(InvalidPayload(_name, "empty secret list response"))
                : Result<List<BwsSecretEntry>>.Success(items);
        }
        catch (JsonException exception)
        {
            return Result<List<BwsSecretEntry>>.Failure(InvalidPayload(_name, exception.Message));
        }
    }

    private static BwsSecretEntry? FindMatchingItem(List<BwsSecretEntry> items, ConfigurationPointer pointer)
    {
        var slashKey = pointer.Value;
        var dotKey = $"{pointer.Namespace}.{pointer.Key}";
        return items.FirstOrDefault(item =>
            string.Equals(item.ResolvedKey, slashKey, StringComparison.Ordinal)
            || string.Equals(item.ResolvedKey, dotKey, StringComparison.Ordinal)
            || string.Equals(item.Id, slashKey, StringComparison.Ordinal));
    }

    private static string? TryGetSetting(IDictionary<string, string> dictionary, string key) =>
        dictionary.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private async ValueTask<Result<string>> RunProcessAsync(
        string binaryPath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        foreach (var (key, value) in environment) startInfo.Environment[key] = value;

        using var process = new Process { StartInfo = startInfo };
        try { process.Start(); }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return Result<string>.Failure(CommandFailed(_name, $"CLI binary could not be started ({exception.GetType().Name})"));
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = (await stdErrTask.ConfigureAwait(false)).Trim();

        if (process.ExitCode == 0)
            return Result<string>.Success(stdOut);

        var category = stdErr.Contains("401", StringComparison.Ordinal) || stdErr.Contains("403", StringComparison.Ordinal)
            || stdErr.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) || stdErr.Contains("unauthenticated", StringComparison.OrdinalIgnoreCase)
            ? ErrorCategory.Forbidden
            : ErrorCategory.Unavailable;

        return Result<string>.Failure(CommandFailed(_name, $"exit code {process.ExitCode}: {stdErr}", category));
    }
}
