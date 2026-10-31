using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Encryption;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Recovery;

/// <summary>Encrypts replay intent with a separate purpose key; ordinary configuration writes cannot mint handles.</summary>
internal sealed class ConfigurationRecovery : IRecovery
{
    private readonly IConfigurationCipher _cipher;
    private readonly string _application;
    private readonly string _root;

    public ConfigurationRecovery(byte[] applicationKey, string application, string root)
    {
        var derived = HKDF.DeriveKey(HashAlgorithmName.SHA256, applicationKey, 32,
            info: Encoding.UTF8.GetBytes("NyssaAI.Configuration.Recovery/v1"));
        try { _cipher = ConfigurationCipherAesGcm.Create(derived).ThrowIfError(); }
        finally { CryptographicOperations.ZeroMemory(derived); }
        _application = application;
        _root = CanonicalRoot(root);
    }

    public Result<ConfigurationRecoveryHandle> Seal(ConfigurationRecoveryState state)
    {
        var serialized = JsonSerializer.Serialize(state, ConfigurationRecoveryJsonContext.Default.ConfigurationRecoveryState);
        var encrypted = _cipher.Seal(serialized, _application, _root);
        return encrypted.IsFailure ? Result<ConfigurationRecoveryHandle>.Failure(encrypted.Errors)
            : Result<ConfigurationRecoveryHandle>.Success(ConfigurationRecoveryHandle.Import(encrypted.Value));
    }

    public Result<ConfigurationRecoveryState> Open(ConfigurationRecoveryHandle handle)
    {
        var opened = _cipher.Open(handle.Export(), _application, _root);
        if (opened.IsFailure)
            return Result<ConfigurationRecoveryState>.Failure(InvalidHandle);
        try
        {
            var state = JsonSerializer.Deserialize(opened.Value, ConfigurationRecoveryJsonContext.Default.ConfigurationRecoveryState);
            return state is { Version: 1, Original: not null, Proposed: not null, Steps: not null }
                ? Result<ConfigurationRecoveryState>.Success(state)
                : Result<ConfigurationRecoveryState>.Failure(InvalidHandle);
        }
        catch (JsonException) { return Result<ConfigurationRecoveryState>.Failure(InvalidHandle); }
    }

    private static string CanonicalRoot(string root)
    {
        var full = Path.GetFullPath(root);
        var path = Path.GetPathRoot(full)!;
        foreach (var component in full[path.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Combine(path, component);
            if (Directory.Exists(path))
                path = new DirectoryInfo(path).ResolveLinkTarget(true)?.FullName ?? path;
        }
        path = Path.TrimEndingDirectorySeparator(path);
        return OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
    }

    private static Error InvalidHandle => Error.FromMessage(
        "The recovery handle is invalid, unsupported, or belongs to a different application, configuration root, or protection key.",
        ErrorCategory.Validation);
}
