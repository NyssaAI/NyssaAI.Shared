using System.Runtime.Versioning;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.OsKeyring;

namespace NyssaAI.Configuration.Tests.OsKeyring;

/// <summary>KeyringMacOsTests exercises the public credential contract against the real macOS keychain.</summary>
[SupportedOSPlatform("macos")]
public sealed class KeyringMacOsTests : KeyringStoreTests
{
    /// <inheritdoc/>
    protected override IKeyring Create() => new KeyringMacOs();

    /// <inheritdoc/>
    protected override void RequirePlatform() =>
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Uses the macOS keychain.");
}
