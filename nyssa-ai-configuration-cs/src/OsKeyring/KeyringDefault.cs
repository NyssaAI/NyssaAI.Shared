using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.OsKeyring;

/// <summary>
/// KeyringDefault selects the credential store for the current platform: <see cref="KeyringSystemdCreds"/>
/// when <c>CREDENTIALS_DIRECTORY</c> is active, Credential Manager on Windows, the keychain on macOS,
/// <see cref="KeyringLinux"/> (<c>systemd-creds</c>) on Linux, and <see cref="KeyringUnsupported"/>
/// anywhere else. A consumer that wants a different store passes its own <see cref="IKeyring"/>
/// instead.
/// </summary>
/// <remarks>
/// Selection is by platform (or active <c>CREDENTIALS_DIRECTORY</c>), not by reachability. A machine
/// whose store is simply not running or missing <c>systemd-creds</c> still gets <see cref="KeyringLinux"/>
/// here and learns at the first call, which is what lets that case be reported as a store outage
/// rather than as an unsupported platform.
/// </remarks>
internal static class KeyringDefault
{
    /// <summary>Creates the keyring for the current platform.</summary>
    public static IKeyring Create()
    {
        if (KeyringSystemdCreds.IsAvailable())
            return new KeyringSystemdCreds();

        if (OperatingSystem.IsWindows())
            return new KeyringWindows();

        if (OperatingSystem.IsMacOS())
            return new KeyringMacOs();

        if (OperatingSystem.IsLinux())
            return new KeyringLinux();

        return new KeyringUnsupported();
    }
}
