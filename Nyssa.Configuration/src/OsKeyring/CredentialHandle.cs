using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace NyssaAI.Configuration.OsKeyring;

/// <summary>
/// Owns a <c>CREDENTIALW</c> buffer returned by <c>CredReadW</c> and frees it with
/// <c>CredFree</c> on release, so the free happens on the miss, error, and throw paths alike.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class CredentialHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>Creates an invalid handle for the marshaller to populate.</summary>
    public CredentialHandle()
        : base(ownsHandle: true)
    {
    }

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        CredFree(handle);
        return true;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(nint buffer);
}
