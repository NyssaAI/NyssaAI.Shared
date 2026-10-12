using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.OsKeyring;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.OsKeyring;

/// <summary>
/// <see cref="KeyringWindows"/> against the Windows Credential Manager of whoever runs it. Manual
/// and opt-in: see <see cref="RealKeyring"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class KeyringWindowsTests : KeyringStoreTests
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    /// <inheritdoc/>
    protected override IKeyring Create() => new KeyringWindows();

    /// <inheritdoc/>
    protected override void RequirePlatform() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Reads the Windows Credential Manager.");

    [Fact]
    public void Get_WhenTheStoreRefusesTheCall_FailsRatherThanReportingAbsence()
    {
        RequirePlatform();
        RealKeyring.RequireOptIn();
        // Credential type 0 is not a valid type, so CredReadW refuses the call with a code that is
        // not ERROR_NOT_FOUND. This is the one path a happy-path test can never reach, and getting
        // it wrong is the failure that matters most: a store that cannot answer reported as an
        // empty store lets a bootstrap conclude the secret does not exist and mint a new one over
        // a machine whose real secret was merely unreadable.
        var keyring = new KeyringWindows(0);

        var found = keyring.Get(RealKeyring.Service, NewKey());

        Assert.True(found.IsFailure);
        var error = Assert.Single(found.Errors);
        Assert.Equal(ErrorCategory.Unexpected, error.Category);
        Assert.Contains("CredReadW failed", error.TechnicalMessage, StringComparison.Ordinal);
        Assert.DoesNotContain($"error {ErrorNotFound}", error.TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_WhenTheStoreRefusesTheCall_ReturnsFailure()
    {
        RequirePlatform();
        RealKeyring.RequireOptIn();
        var keyring = new KeyringWindows(0);

        var deleted = keyring.Delete(RealKeyring.Service, NewKey());

        Assert.True(deleted.IsFailure);
        Assert.Contains("CredDeleteW failed", Assert.Single(deleted.Errors).TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Set_WithASecretPastTheBlobLimit_FailsRatherThanTruncating()
    {
        var keyring = Ready();
        var key = NewKey();
        // CRED_MAX_CREDENTIAL_BLOB_SIZE is 2560 bytes, so 1400 characters is over it as UTF-16.
        // The limit is real and low enough for a long token to reach, and the only safe answer is
        // a refusal: a store that quietly kept the first 2560 bytes would hand back a secret that
        // looks well-formed and decrypts nothing.
        var secret = new string('s', 1400);

        var stored = keyring.Set(RealKeyring.Service, key, secret);

        Assert.True(stored.IsFailure);
        Assert.Contains("CredWriteW failed", Assert.Single(stored.Errors).TechnicalMessage, StringComparison.Ordinal);
        Assert.Null(keyring.Get(RealKeyring.Service, key).Value);
    }

    [Fact]
    public void Set_FilesTheSecretAsUtf16UnderServiceSlashKey()
    {
        var keyring = Ready();
        var key = NewKey();
        const string Secret = "blob é中";

        Assert.True(keyring.Set(RealKeyring.Service, key, Secret).IsSuccess);

        // The documented interop contract, read back without going through the adapter: the target
        // name an operator would type into `cmdkey /generic:`, and a blob decoded as UTF-16LE. An
        // adapter that agreed with itself on some other spelling would pass every round-trip test
        // in the base class and still be invisible to the tools the docs point operators at.
        var blob = ReadRawBlob($"{RealKeyring.Service}/{key}");
        Assert.Equal(Encoding.Unicode.GetBytes(Secret), blob);
        Assert.Equal(Secret, Encoding.Unicode.GetString(blob));
    }

    [Fact]
    public void Get_ReadsACredentialAnotherWriterFiledAtTheSameAddress()
    {
        var keyring = Ready();
        var key = NewKey();
        const string Secret = "placed by an operator";

        WriteRawCredential($"{RealKeyring.Service}/{key}", Secret);

        // The other direction of the same contract: a secret an operator placed with cmdkey, or a
        // provisioning script placed with CredWriteW, is one this adapter can find.
        Assert.Equal(Secret, keyring.Get(RealKeyring.Service, key).Value);
    }

    // Reads the credential blob straight out of Credential Manager, so the assertion is about what
    // is actually stored rather than about what the adapter says it stored.
    private static byte[] ReadRawBlob(string targetName)
    {
        Assert.True(CredRead(targetName, CredentialTypeGeneric, 0, out var handle),
            $"no credential is filed under '{targetName}' (Win32 {Marshal.GetLastPInvokeError()})");

        try
        {
            var credential = Marshal.PtrToStructure<CredentialW>(handle);
            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return blob;
        }
        finally
        {
            CredFree(handle);
        }
    }

    private static void WriteRawCredential(string targetName, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var name = Marshal.StringToHGlobalUni(targetName);
        var blobPointer = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var credential = new CredentialW
            {
                Type = CredentialTypeGeneric,
                TargetName = name,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = CredentialPersistLocalMachine,
            };

            Assert.True(CredWrite(ref credential, 0),
                $"CredWriteW failed with Win32 {Marshal.GetLastPInvokeError()}");
        }
        finally
        {
            Marshal.FreeHGlobal(blobPointer);
            Marshal.FreeHGlobal(name);
        }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string targetName, uint type, uint flags, out nint credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(ref CredentialW credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(nint buffer);

    // Mirrors CREDENTIALW, declared here rather than shared with the adapter so this suite checks
    // the stored bytes independently instead of agreeing with the layout it is testing.
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
