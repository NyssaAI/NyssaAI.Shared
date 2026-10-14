using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace NyssaAI.Logging.FileLogging;

/// <summary>
/// OwnerOnlyAccess restricts a log file and its directory to the account that created them,
/// because log records carry exception text and structured fields. On Unix that is mode 0600
/// for the file and 0700 for the directory; on Windows it replaces the ACL with a current-user
/// grant and removes inheritance. Only what the library created, or owns, is restricted: a
/// pre-existing caller-supplied directory keeps the permissions its operator gave it.
/// </summary>
internal static class OwnerOnlyAccess
{
    /// <summary>Mode 0600 - owner read and write, for the log file itself.</summary>
    public const UnixFileMode OwnerReadWrite =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Mode 0700 - owner read, write, and traverse, for the log directory.</summary>
    public const UnixFileMode OwnerReadWriteExecute =
        OwnerReadWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// Create the log directory restricted to the owner. A directory this call creates is
    /// restricted; one that already exists keeps the permissions its operator gave it, because
    /// tightening <c>/var/log</c> or <c>C:\ProgramData\Logs</c> would lock every other service
    /// out. Best-effort: a failure is reported to stderr and never thrown, because logging must
    /// not take the host process down.
    /// </summary>
    /// <param name="path">The directory to create or reuse.</param>
    public static void CreateDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            ApplyWindowsDirectoryAcl(path);
            return;
        }

        // The mode argument applies only when this call creates the directory.
        Directory.CreateDirectory(path, OwnerReadWriteExecute);
        RestrictUnixPath(path, OwnerReadWriteExecute);
    }

    /// <summary>
    /// Restrict an existing log file to the owner. On Unix the mode is also applied at
    /// creation time by <see cref="LogFile"/>; this call additionally tightens a file left
    /// behind by an earlier run. The file is always the library's own artifact, so it is
    /// tightened wherever it lives. Best-effort, as above.
    /// </summary>
    public static void ApplyToFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            ApplyWindowsFileAcl(path);
            return;
        }

        RestrictUnixPath(path, OwnerReadWrite);
    }

    [UnsupportedOSPlatform("windows")]
    private static void RestrictUnixPath(string path, UnixFileMode mode)
    {
        try
        {
            File.SetUnixFileMode(path, mode);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            StderrDiagnostics.Warning(path, $"failed to restrict permissions: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindowsFileAcl(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            // Replace the access rules rather than editing the old descriptor: inherited and
            // explicit grants from an earlier run must both be removed from the owned log file.
            var security = new FileSecurity();

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            AllowCurrentUserOnly(security.AddAccessRule);

            fileInfo.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            StderrDiagnostics.Warning(path, $"failed to apply Windows file permissions: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindowsDirectoryAcl(string path)
    {
        try
        {
            var dirInfo = new DirectoryInfo(path);
            var security = new DirectorySecurity();

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            AllowCurrentUserOnly(rule => security.AddAccessRule(rule), inheritable: true);

            dirInfo.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            StderrDiagnostics.Warning(path, $"failed to apply Windows directory permissions: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AllowCurrentUserOnly(
        Action<FileSystemAccessRule> addRule,
        bool inheritable = false)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var currentUser = identity.User;
        if (currentUser is null)
        {
            return;
        }

        addRule(inheritable
            ? new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow)
            : new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
    }
}
