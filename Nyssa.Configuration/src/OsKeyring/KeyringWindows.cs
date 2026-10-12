using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using NyssaAI.Results;
using NyssaAI.Configuration.Contracts;


namespace NyssaAI.Configuration.OsKeyring;

/// <summary>
/// KeyringWindows reads generic credentials from the Windows Credential Manager. A secret filed
/// under service <c>S</c> and key <c>K</c> is the generic credential whose target name is
/// <c>S/K</c>, as written by <c>cmdkey /generic:S/K</c> or <c>CredWriteW</c>.
/// </summary>
/// <remarks>
/// The credential blob is decoded as UTF-16LE, the convention for text stored through
/// <c>CredWriteW</c>. <c>ERROR_NOT_FOUND</c> is the one code that means "nothing filed here";
/// every other failure is reported as the store being unable to answer, so a misread code can
/// never be mistaken for an absent secret.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed partial class KeyringWindows : IKeyring
{
    private const uint CredentialTypeGeneric = 1;
    private const int ErrorNotFound = 1168;

    // CRED_PERSIST_LOCAL_MACHINE: the credential belongs to this user on this machine and survives
    // logoff, which is the lifetime a configuration key needs. Enterprise persistence would roam it
    // to every machine the profile reaches, widening custody rather than keeping it local.
    private const uint CredentialPersistLocalMachine = 2;

    private readonly uint _credentialType;

    /// <summary>Creates a keyring over the generic credentials of the current user.</summary>
    public KeyringWindows()
        : this(CredentialTypeGeneric)
    {
    }

    // Lets a test ask for a credential type the API refuses, which is the only way to observe
    // the non-ERROR_NOT_FOUND failure path without corrupting the developer's credential store.
    internal KeyringWindows(uint credentialType) => _credentialType = credentialType;

    /// <inheritdoc/>
    public Result<string?> Get(string service, string key)
    {
        var target = KeyringAddress.Compose(service, key);
        if (target.IsFailure)
            return Result<string?>.Failure(target.Errors);

        // This is the adapter boundary: the native call reports through the last-error code, and
        // that code is read in the statement immediately after the call, before anything else can
        // overwrite it.
        CredentialHandle? credential = null;
        try
        {
            var found = CredRead(target.Value, _credentialType, 0, out credential);
            var error = Marshal.GetLastPInvokeError();
            if (!found)
            {
                return error == ErrorNotFound
                    ? Result<string?>.Success(null)
                    : Result<string?>.Failure(StoreUnavailable(error));
            }

            return Result<string?>.Success(ReadBlob(credential));
        }
        finally
        {
            credential?.Dispose();
        }
    }

    /// <inheritdoc/>
    public Result Set(string service, string key, string secret)
    {
        var target = KeyringAddress.Compose(service, key);
        if (target.IsFailure)
            return Result.Failure(target.Errors);

        // This is the adapter boundary: the native call reports through the last-error code, read
        // immediately after the call. The blob holds the secret, so both the managed array and the
        // unmanaged copy are zeroed before release rather than left for the allocator to reuse.
        var blob = Encoding.Unicode.GetBytes(secret);
        nint targetName = 0;
        nint blobPointer = 0;
        try
        {
            targetName = Marshal.StringToHGlobalUni(target.Value);
            blobPointer = Marshal.AllocHGlobal(blob.Length);
            Marshal.Copy(blob, 0, blobPointer, blob.Length);

            var credential = new CredentialW
            {
                Type = _credentialType,
                TargetName = targetName,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = CredentialPersistLocalMachine,
            };

            var written = CredWrite(ref credential, 0);
            var error = Marshal.GetLastPInvokeError();
            return written
                ? Result.Success()
                : Result.Failure(StoreNotWritable(error));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
            if (blobPointer != 0)
            {
                ZeroUnmanaged(blobPointer, blob.Length);
                Marshal.FreeHGlobal(blobPointer);
            }

            if (targetName != 0)
                Marshal.FreeHGlobal(targetName);
        }
    }

    /// <inheritdoc/>
    public Result Delete(string service, string key)
    {
        var target = KeyringAddress.Compose(service, key);
        if (target.IsFailure)
            return Result.Failure(target.Errors);

        var deleted = CredDelete(target.Value, _credentialType, 0);
        var error = Marshal.GetLastPInvokeError();
        return deleted || error == ErrorNotFound
            ? Result.Success()
            : Result.Failure(StoreNotDeletable(error));
    }

    /// <summary>The failure returned when Credential Manager refuses deletion.</summary>
    /// <param name="win32Error">The Win32 error code that was returned.</param>
    public static Error StoreNotDeletable(int win32Error) => Error.FromMessages(
        "The credential could not be deleted.",
        $"keyring: CredDeleteW failed with Win32 error {win32Error}",
        ErrorCategory.Unexpected);

    /// <summary>The failure returned when Credential Manager reports anything but "not found".</summary>
    /// <param name="win32Error">The Win32 error code that was returned.</param>
    public static Error StoreUnavailable(int win32Error) => Error.FromMessages(
        "The credential store could not be read.",
        $"keyring: CredReadW failed with Win32 error {win32Error}",
        ErrorCategory.Unexpected);

    /// <summary>The failure returned when Credential Manager refuses a write.</summary>
    /// <param name="win32Error">The Win32 error code that was returned.</param>
    public static Error StoreNotWritable(int win32Error) => Error.FromMessages(
        "The credential store could not be written.",
        $"keyring: CredWriteW failed with Win32 error {win32Error}",
        ErrorCategory.Unexpected);

    private static unsafe void ZeroUnmanaged(nint buffer, int length) =>
        new Span<byte>((void*)buffer, length).Clear();

    private static unsafe string ReadBlob(CredentialHandle handle)
    {
        var credential = (CredentialW*)handle.DangerousGetHandle();
        if (credential->CredentialBlob == 0 || credential->CredentialBlobSize == 0)
            return string.Empty;

        var bytes = new ReadOnlySpan<byte>((void*)credential->CredentialBlob, (int)credential->CredentialBlobSize);
        return Encoding.Unicode.GetString(bytes);
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string targetName, uint type, uint flags, out CredentialHandle credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(ref CredentialW credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string targetName, uint type, uint flags);

    // Mirrors CREDENTIALW. LastWritten is a FILETIME, two DWORDs, kept as such so the layout
    // matches on both 32- and 64-bit.
    [StructLayout(LayoutKind.Sequential)]
    private struct CredentialW
    {
        public uint Flags;
        public uint Type;
        public nint TargetName;
        public nint Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }
}
