using NyssaAI.Logging.FileLogging;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace NyssaAI.Logging.Tests;

/// <summary>
/// These assert the permissions actually applied, not merely that the call did not throw.
/// The previous DoesNotThrow tests passed against an empty method body for the entire time
/// the Unix implementation was a placeholder.
/// </summary>
public sealed class OwnerOnlyAccessTests
{
    private const UnixFileMode SharedDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void CreateDirectory_OnUnix_RestrictsTheDirectoryToItsOwner()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes are only observable on Unix.");

        var directory = TempDirectoryPath();
        try
        {
            OwnerOnlyAccess.CreateDirectory(directory);

            Assert.Equal(
                OwnerOnlyAccess.OwnerReadWriteExecute,
                File.GetUnixFileMode(directory));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void CreateDirectory_OnUnixWhenACallerDirectoryAlreadyExists_KeepsItsMode()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes are only observable on Unix.");

        // Run as root against /var/log this would otherwise chmod 0700 the system log directory.
        var directory = TempDirectoryPath();
        try
        {
            Directory.CreateDirectory(directory);
            File.SetUnixFileMode(directory, SharedDirectoryMode);

            OwnerOnlyAccess.CreateDirectory(directory);

            Assert.Equal(SharedDirectoryMode, File.GetUnixFileMode(directory));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Initialize_OnUnix_CreatesTheLogFileReadableOnlyByItsOwner()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes are only observable on Unix.");

        var directory = TempDirectoryPath();
        var logPath = Path.Combine(directory, "test.log");
        var logFile = new LogFile(logPath);
        try
        {
            Assert.Null(logFile.Initialize());

            Assert.Equal(OwnerOnlyAccess.OwnerReadWrite, File.GetUnixFileMode(logPath));
        }
        finally
        {
            logFile.Dispose();
            Delete(directory);
        }
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void ApplyToFile_OnUnixForAFileLeftByAnEarlierRun_TightensIt()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes are only observable on Unix.");

        var directory = TempDirectoryPath();
        var logPath = Path.Combine(directory, "test.log");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(logPath, string.Empty);
            File.SetUnixFileMode(
                logPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite
                    | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            OwnerOnlyAccess.ApplyToFile(logPath);

            Assert.Equal(OwnerOnlyAccess.OwnerReadWrite, File.GetUnixFileMode(logPath));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void CreateDirectory_OnWindows_LeavesOnlyTheCurrentUserWithAccess()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows ACLs are only observable on Windows.");

        var directory = TempDirectoryPath();
        try
        {
            OwnerOnlyAccess.CreateDirectory(directory);

            var security = new DirectoryInfo(directory).GetAccessControl();
            Assert.True(security.AreAccessRulesProtected);
            AssertOnlyCurrentUser(security);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void CreateDirectory_OnWindowsWhenACallerDirectoryAlreadyExists_KeepsItsInheritedPermissions()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows ACLs are only observable on Windows.");

        // Run as Administrator against C:\ProgramData\Logs this would otherwise strip the
        // inherited ACL from a shared directory.
        var directory = TempDirectoryPath();
        try
        {
            Directory.CreateDirectory(directory);

            OwnerOnlyAccess.CreateDirectory(directory);

            Assert.False(new DirectoryInfo(directory).GetAccessControl().AreAccessRulesProtected);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Initialize_OnWindows_LeavesOnlyTheCurrentUserWithAccessToTheLogFile()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows ACLs are only observable on Windows.");

        var directory = TempDirectoryPath();
        var logPath = Path.Combine(directory, "test.log");
        var logFile = new LogFile(logPath);
        try
        {
            Assert.Null(logFile.Initialize());

            var security = new FileInfo(logPath).GetAccessControl();
            Assert.True(security.AreAccessRulesProtected);
            AssertOnlyCurrentUser(security);
        }
        finally
        {
            logFile.Dispose();
            Delete(directory);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AssertOnlyCurrentUser(FileSystemSecurity security)
    {
        var rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            targetType: typeof(SecurityIdentifier));

        var rule = Assert.IsType<FileSystemAccessRule>(Assert.Single(rules));
        Assert.Equal(WindowsIdentity.GetCurrent().User, rule.IdentityReference);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void ApplyToFile_ExistingExplicitForeignGrant_RemovesItWithoutChangingDirectoryPermissions()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows ACLs are only observable on Windows.");

        var directory = TempDirectoryPath();
        var logPath = Path.Combine(directory, "test.log");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(logPath, "existing record");
            var directoryAcl = new DirectoryInfo(directory).GetAccessControl()
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            var file = new FileInfo(logPath);
            var security = file.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.ReadData,
                AccessControlType.Allow));
            file.SetAccessControl(security);

            OwnerOnlyAccess.ApplyToFile(logPath);

            AssertOnlyCurrentUser(file.GetAccessControl());
            Assert.Equal(directoryAcl, new DirectoryInfo(directory).GetAccessControl()
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access));
            Assert.Equal("existing record", File.ReadAllText(logPath));
        }
        finally
        {
            Delete(directory);
        }
    }

    private static string TempDirectoryPath() =>
        Path.Combine(Path.GetTempPath(), $"LoggingTest_{Guid.NewGuid():N}");

    private static void Delete(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
