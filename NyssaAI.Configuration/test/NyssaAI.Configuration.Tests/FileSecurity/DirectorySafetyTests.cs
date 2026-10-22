using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using NyssaAI.Results;

using NyssaAI.Configuration.Values;
using NyssaAI.Configuration.FileSecurity;
using NyssaAI.Configuration.Tests.Support;

namespace NyssaAI.Configuration.Tests.FileSecurity;

/// <summary>
/// The guard that refuses a configuration directory other accounts can write to.
/// </summary>
public sealed class DirectorySafetyTests
{
    [Fact]
    public void Verify_ForADirectoryThisAccountJustCreated_Succeeds()
    {
        using var directory = new TemporaryDirectory();

        Assert.True(DirectorySafety.Verify(directory.Path).IsSuccess);
    }

    [Fact]
    public void Verify_ForADirectoryThatDoesNotExist_Succeeds()
    {
        // Not a failure: resolution creates the directory, and creating it makes this account the
        // owner. Refusing here would refuse every first run.
        var absent = Path.Combine(Path.GetTempPath(), $"nyssa-absent-{Guid.NewGuid():N}");

        Assert.True(DirectorySafety.Verify(absent).IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_WithNoDirectory_Succeeds(string directory)
    {
        Assert.True(DirectorySafety.Verify(directory).IsSuccess);
    }

    [Fact]
    public void Verify_WhenEveryoneCanWrite_IsRefusedAsForbidden()
    {
        // The case the guard exists for. An account that can write here swaps a namespace document
        // for one of its own, and every value the reader gets back is well-formed and correctly
        // sealed -- just not what the owner wrote.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Grants a Windows ACL; the Unix path is mode-based.");
        // SkipUnless has already ended the test off Windows; this is what tells the platform
        // analyzer the same thing.
        if (!OperatingSystem.IsWindows()) { return; }

        using var directory = new TemporaryDirectory();
        GrantEveryoneWrite(directory.Path);

        var verified = DirectorySafety.Verify(directory.Path);

        Assert.True(verified.IsFailure);
        var error = Assert.Single(verified.Errors);
        Assert.Equal(ErrorCategory.Forbidden, error.Category);
        Assert.Contains("writable by", error.TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_WhenEveryoneIsDeniedWrite_StillSucceeds()
    {
        // A deny rule is not a grant. Treating every rule mentioning a multi-user identity as a
        // violation would refuse directories that are locked down harder than the default.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Grants a Windows ACL; the Unix path is mode-based.");
        // SkipUnless has already ended the test off Windows; this is what tells the platform
        // analyzer the same thing.
        if (!OperatingSystem.IsWindows()) { return; }

        using var directory = new TemporaryDirectory();
        DenyEveryoneWrite(directory.Path);

        Assert.True(DirectorySafety.Verify(directory.Path).IsSuccess);
    }

    [Fact]
    public void DocumentFactory_InAnUnsafeDirectory_RefusesBeforeHandingOutADocument()
    {
        // The guard has to run where the directory stops being a candidate and starts being read
        // from, which is here rather than at resolution.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Grants a Windows ACL; the Unix path is mode-based.");
        // SkipUnless has already ended the test off Windows; this is what tells the platform
        // analyzer the same thing.
        if (!OperatingSystem.IsWindows()) { return; }

        using var directory = new TemporaryDirectory();
        GrantEveryoneWrite(directory.Path);

        var created = new ConfigurationDocumentFactoryLocalFile(directory.Path).Create("service");

        Assert.True(created.IsFailure);
        Assert.Equal(ErrorCategory.Forbidden, Assert.Single(created.Errors).Category);
    }

    [Fact]
    public void VerifyPrivateFile_WhenEveryoneCanRead_IsRefusedNamingTheFix()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Grants a Windows ACL; the Unix path is mode-based.");
        if (!OperatingSystem.IsWindows()) { return; }

        using var directory = new TemporaryDirectory();
        var filePath = Path.Combine(directory.Path, "config-manifest.json");
        File.WriteAllText(filePath, "{}");
        GrantEveryoneRead(filePath);

        var verified = DirectorySafety.VerifyPrivateFile(filePath);

        Assert.True(verified.IsFailure);
        var error = Assert.Single(verified.Errors);
        Assert.Equal(ErrorCategory.Forbidden, error.Category);
        Assert.Contains("is readable by", error.TechnicalMessage, StringComparison.Ordinal);
        Assert.Contains("icacls", error.TechnicalMessage, StringComparison.Ordinal);
    }

    [SupportedOSPlatform("windows")]
    private static void GrantEveryoneRead(string path)
    {
        var info = new FileInfo(path);
        var security = FileSystemAclExtensions.GetAccessControl(info);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.ReadData,
            AccessControlType.Allow));
        FileSystemAclExtensions.SetAccessControl(info, security);
    }

    [SupportedOSPlatform("windows")]
    private static void GrantEveryoneWrite(string path)
    {
        var info = new DirectoryInfo(path);
        var security = FileSystemAclExtensions.GetAccessControl(info);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.WriteData,
            AccessControlType.Allow));
        FileSystemAclExtensions.SetAccessControl(info, security);
    }

    [SupportedOSPlatform("windows")]
    private static void DenyEveryoneWrite(string path)
    {
        var info = new DirectoryInfo(path);
        var security = FileSystemAclExtensions.GetAccessControl(info);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.WriteData,
            AccessControlType.Deny));
        FileSystemAclExtensions.SetAccessControl(info, security);
    }

}
