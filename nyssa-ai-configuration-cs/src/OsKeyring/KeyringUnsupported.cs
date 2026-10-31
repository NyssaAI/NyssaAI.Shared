using System.Runtime.InteropServices;
using NyssaAI.Results;
using NyssaAI.Configuration.Contracts;


namespace NyssaAI.Configuration.OsKeyring;

/// <summary>
/// KeyringUnsupported is the keyring on a platform with no supported credential store. Every read,
/// write and deletion reports a failure naming the platform, so construction stays total and a
/// consumer learns at the first call rather than crashing at startup.
/// </summary>
internal sealed class KeyringUnsupported : IKeyring
{
    /// <inheritdoc/>
    public Result<string?> Get(string service, string key) =>
        Result<string?>.Failure(PlatformUnsupported(RuntimeInformation.OSDescription));

    /// <inheritdoc/>
    public Result Set(string service, string key, string secret) =>
        Result.Failure(PlatformUnsupported(RuntimeInformation.OSDescription));

    /// <inheritdoc/>
    public Result Delete(string service, string key) =>
        Result.Failure(PlatformUnsupported(RuntimeInformation.OSDescription));

    /// <summary>The failure returned on every read, write and deletion.</summary>
    /// <param name="platform">The platform description the runtime reports.</param>
    public static Error PlatformUnsupported(string platform) => Error.FromMessages(
        "The credential store is not supported on this platform.",
        $"keyring: no credential store implementation exists for '{platform}'; Windows, macOS and Linux are supported",
        ErrorCategory.Unexpected);
}
