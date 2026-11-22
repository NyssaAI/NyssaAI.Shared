using System.Globalization;
using System.Text.Json;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Manifest;

/// <summary>
/// Validates and normalizes root <c>keyVaults</c> definitions and their encrypted <c>aesLocal</c>
/// bootstrap secret envelopes.
/// </summary>
internal static class ManifestVaultValidator
{
    private const byte ExpectedCipherFormatVersion = 2;
    private const int MinimumCipherPayloadBytes = 29;

    internal static Result ValidateVaultName(string? vaultName)
    {
        if (string.IsNullOrWhiteSpace(vaultName)
            || !string.Equals(vaultName, vaultName.Trim(), StringComparison.Ordinal)
            || vaultName.Any(ch => ch is '/' or '\\' || char.IsWhiteSpace(ch) || char.IsControl(ch)))
        {
            return Result.Failure(ValidationFailure(
                "manifest: vault name must be a non-blank trimmed identifier without '/', '\\', whitespace, or control characters"));
        }

        if (string.Equals(vaultName, "aesLocal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(vaultName, "literal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(vaultName, "oauth", StringComparison.OrdinalIgnoreCase)
            || string.Equals(vaultName, "default", StringComparison.OrdinalIgnoreCase)
            || string.Equals(vaultName, "plaintext", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(ValidationFailure(
                $"manifest: vault name '{vaultName}' is reserved and cannot be used for a cloud key vault"));
        }

        return Result.Success();
    }

    internal static Result ValidateVaultKind(string vaultName, string? kind) =>
        string.IsNullOrWhiteSpace(kind) || kind.Any(char.IsControl)
            ? Result.Failure(ValidationFailure(
                $"manifest: keyVaults.{vaultName}.kind is required and must be a non-blank string without control characters"))
            : Result.Success();

    internal static Result ValidateEndpointString(string vaultName, string? endpoint)
    {
        if (endpoint is null)
            return Result.Success();

        if (string.IsNullOrWhiteSpace(endpoint)
            || endpoint.Any(char.IsControl)
            || !endpoint.Contains("://", StringComparison.Ordinal)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)
            || string.IsNullOrEmpty(parsed.Authority))
        {
            return Result.Failure(ValidationFailure(
                $"manifest: vault '{vaultName}' names endpoint '{SanitizeForError(endpoint)}', which is not an absolute URI"));
        }

        return Result.Success();
    }

    internal static Result<string?> ValidateAndNormalizeCredential(string vaultName, string? credential)
    {
        if (credential is null)
            return Result<string?>.Success(null);

        var normalized = credential.StartsWith("oauth://", StringComparison.Ordinal)
            ? credential["oauth://".Length..]
            : credential;

        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Any(ch => char.IsControl(ch) || char.IsWhiteSpace(ch))
            || ConfigurationPointer.Parse(vaultName, normalized).IsFailure)
        {
            return Result<string?>.Failure(ValidationFailure(
                $"manifest: vault '{vaultName}' names credential '{SanitizeForError(credential)}', which is not a namespace/key address"));
        }

        return Result<string?>.Success(normalized);
    }

    internal static Result ValidateCacheTtl(
        string vaultName,
        int? cacheTtlSeconds,
        IReadOnlyDictionary<string, string>? settings,
        string? manifestPath = null)
    {
        if (cacheTtlSeconds is < 0)
        {
            var location = manifestPath is null ? "manifest" : $"manifest: '{manifestPath}'";
            return Result.Failure(ValidationFailure(
                $"{location} names cacheTTLSeconds '{cacheTtlSeconds.Value}' for vault '{vaultName}', which is not a non-negative integer"));
        }

        if (settings is not null && settings.TryGetValue("cacheTTLSeconds", out var rawSettingTtl))
        {
            if (string.IsNullOrEmpty(rawSettingTtl)
                || !rawSettingTtl.All(char.IsAsciiDigit)
                || !int.TryParse(rawSettingTtl, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                var location = manifestPath is null ? "manifest" : $"manifest: '{manifestPath}'";
                return Result.Failure(ValidationFailure(
                    $"{location} names settings['cacheTTLSeconds'] '{SanitizeForError(rawSettingTtl)}' for vault '{vaultName}', which is not a non-negative integer"));
            }
        }

        return Result.Success();
    }

    internal static Result ValidateNormalizedSecrets(string vaultName, IReadOnlyDictionary<string, ManifestSecretEnvelope> secrets)
    {
        foreach (var (secretKey, envelope) in secrets)
        {
            if (string.IsNullOrWhiteSpace(secretKey) || secretKey.Any(char.IsControl))
            {
                return Result.Failure(ValidationFailure(
                    $"manifest: keyVaults.{vaultName}.secrets contains an invalid secret key"));
            }

            if (envelope is null
                || !envelope.Encrypted
                || (envelope.Provider is not null && !string.Equals(envelope.Provider, "aesLocal", StringComparison.Ordinal))
                || !IsValidCiphertextPayload(envelope.Value))
            {
                return Result.Failure(SecretEnvelopeInvalid(vaultName, secretKey));
            }
        }

        return Result.Success();
    }

    internal static bool IsValidCiphertextPayload(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length % 4 == 1)
            return false;

        foreach (var ch in value)
        {
            var validChar = (ch >= 'A' && ch <= 'Z')
                || (ch >= 'a' && ch <= 'z')
                || (ch >= '0' && ch <= '9')
                || ch is '-' or '_';
            if (!validChar)
                return false;
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        var remainder = padded.Length % 4;
        if (remainder == 2)
            padded += "==";
        else if (remainder == 3)
            padded += "=";

        try
        {
            var decoded = Convert.FromBase64String(padded);
            return decoded.Length >= MinimumCipherPayloadBytes && decoded[0] == ExpectedCipherFormatVersion;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static Result<VaultManifestEntry> ValidateAndNormalizeVault(
        string path,
        string vaultName,
        VaultManifestEntry entry)
    {
        var nameValid = ValidateVaultName(vaultName);
        if (nameValid.IsFailure)
            return Result<VaultManifestEntry>.Failure(nameValid.Errors);

        if (entry.Name is not null && !string.Equals(entry.Name, vaultName, StringComparison.Ordinal))
        {
            return Result<VaultManifestEntry>.Failure(ValidationFailure(
                $"manifest: keyVaults.{vaultName}.name '{SanitizeForError(entry.Name)}' must match map key '{vaultName}'"));
        }

        var kindValid = ValidateVaultKind(vaultName, entry.Kind);
        if (kindValid.IsFailure)
            return Result<VaultManifestEntry>.Failure(kindValid.Errors);

        var endpointValid = ValidateEndpointString(vaultName, entry.Endpoint);
        if (endpointValid.IsFailure)
            return Result<VaultManifestEntry>.Failure(endpointValid.Errors);

        var credential = ValidateAndNormalizeCredential(vaultName, entry.Credential);
        if (credential.IsFailure)
            return Result<VaultManifestEntry>.Failure(credential.Errors);

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (settingKey, settingValue) in entry.Settings ?? [])
        {
            if (string.IsNullOrWhiteSpace(settingKey) || settingKey.Any(char.IsControl) || settingValue is null)
            {
                return Result<VaultManifestEntry>.Failure(ValidationFailure(
                    $"manifest: keyVaults.{vaultName}.settings contains an invalid key or null value"));
            }

            settings[settingKey] = settingValue;
        }

        var ttlValid = ValidateCacheTtl(vaultName, entry.CacheTTLSeconds, settings, path);
        if (ttlValid.IsFailure)
            return Result<VaultManifestEntry>.Failure(ttlValid.Errors);

        var secrets = new Dictionary<string, ManifestSecretEnvelope>(StringComparer.Ordinal);
        if (entry.RawSecrets is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } rawSecrets)
        {
            if (rawSecrets.ValueKind != JsonValueKind.Object)
            {
                return Result<VaultManifestEntry>.Failure(ValidationFailure(
                    $"manifest: keyVaults.{vaultName}.secrets must be a JSON object"));
            }

            foreach (var property in rawSecrets.EnumerateObject())
            {
                var secretKey = property.Name;
                if (string.IsNullOrWhiteSpace(secretKey) || secretKey.Any(char.IsControl))
                {
                    return Result<VaultManifestEntry>.Failure(ValidationFailure(
                        $"manifest: keyVaults.{vaultName}.secrets contains an invalid secret key"));
                }

                if (!TryParseSecretEnvelope(property.Value, out var envelope))
                    return Result<VaultManifestEntry>.Failure(SecretEnvelopeInvalid(vaultName, secretKey));

                secrets[secretKey] = envelope;
            }
        }
        else if (entry.Secrets is not null)
        {
            var normalizedValid = ValidateNormalizedSecrets(vaultName, entry.Secrets);
            if (normalizedValid.IsFailure)
                return Result<VaultManifestEntry>.Failure(normalizedValid.Errors);

            foreach (var (secretKey, envelope) in entry.Secrets)
                secrets[secretKey] = envelope;
        }

        return Result<VaultManifestEntry>.Success(entry with
        {
            Name = vaultName,
            Credential = credential.Value,
            Settings = settings,
            Secrets = secrets,
            RawSecrets = null,
        });
    }

    private static bool TryParseSecretEnvelope(JsonElement element, out ManifestSecretEnvelope envelope)
    {
        envelope = default!;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        if (!element.TryGetProperty("encrypted", out var encryptedProp) || encryptedProp.ValueKind != JsonValueKind.True)
            return false;

        string? provider = "aesLocal";
        if (element.TryGetProperty("provider", out var providerProp) && providerProp.ValueKind != JsonValueKind.Null)
        {
            if (providerProp.ValueKind != JsonValueKind.String
                || !string.Equals(providerProp.GetString(), "aesLocal", StringComparison.Ordinal))
            {
                return false;
            }

            provider = "aesLocal";
        }

        if (!element.TryGetProperty("value", out var valueProp) || valueProp.ValueKind != JsonValueKind.String)
            return false;

        var ciphertext = valueProp.GetString();
        if (!IsValidCiphertextPayload(ciphertext))
            return false;

        envelope = new ManifestSecretEnvelope
        {
            Value = ciphertext!,
            Provider = provider,
            Encrypted = true,
        };
        return true;
    }

    internal static string SanitizeForError(string value) =>
        new(value.Select(ch => char.IsControl(ch) ? '?' : ch).ToArray());

    internal static Error ValidationFailure(string technicalMessage) => Error.FromMessages(
        "The configuration manifest is malformed.",
        technicalMessage,
        ErrorCategory.Validation);

    /// <summary>The failure returned when a vault secret is plaintext or not a valid encrypted <c>aesLocal</c> envelope.</summary>
    public static Error SecretEnvelopeInvalid(string vaultName, string secretKey) => Error.FromMessages(
        "The configuration manifest contains an unencrypted or malformed vault secret.",
        $"manifest: keyVaults.{vaultName}.secrets.{SanitizeForError(secretKey)} must be an encrypted aesLocal envelope; plaintext secrets are forbidden in config-manifest.json",
        ErrorCategory.Validation);
}
