using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using NyssaAI.Results;

namespace NyssaAI.Configuration.FileSecurity;

/// <summary>
/// Creates configuration directories and files that only the current account can read or write.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Directory.CreateDirectory(string)"/> inherits the parent's permissions, and on a
/// default Windows volume the root grants <c>Authenticated Users</c> modify rights to everything
/// beneath it. A configuration directory created that way is refused by
/// <see cref="DirectorySafety.Verify"/> on its second use, so every directory this feature creates
/// comes through here instead: on Windows with a protected DACL granting full control to the
/// current user, SYSTEM, and Administrators only; on Unix with mode <c>0700</c>. Files are created
/// <c>0600</c> on Unix; on Windows a file inherits the protected directory's rules.
/// </para>
/// <para>
/// An existing directory is left as it is. Changing permissions on a directory another tool
/// created is not this feature's call; <see cref="DirectorySafety.Verify"/> reports whether it is
/// acceptable.
/// </para>
/// </remarks>
internal static class ProtectedFileSystem
{
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode0600 = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Creates a directory readable and writable by the current account only.</summary>
    /// <param name="directory">The directory to create; missing parents are created too.</param>
    /// <returns>Success when the directory exists afterwards, protected if it was created here.</returns>
    public static Result CreateDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return Result.Failure(DirectoryNotCreatable(directory, new ArgumentException("The directory path is blank.")));

        // This is the adapter boundary: the file system and the security APIs throw here and
        // nowhere above it.
        try
        {
            var fullPath = Path.GetFullPath(directory);
            if (Directory.Exists(fullPath))
                return Result.Success();

            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            if (OperatingSystem.IsWindows())
                CreateWindows(fullPath);
            else
                CreateUnix(fullPath);

            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or PlatformNotSupportedException or PrivilegeNotHeldException or ArgumentException)
        {
            return Result.Failure(DirectoryNotCreatable(directory, exception));
        }
    }

    /// <summary>Writes a file readable by the current account only, replacing any existing file.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="content">The text to write, encoded as UTF-8 without a byte-order mark.</param>
    /// <remarks>The synchronous twin of <see cref="WriteAllTextAsync"/>, for the callers that are themselves synchronous.</remarks>
    public static Result WriteAllText(string path, string content)
    {
        // This is the adapter boundary: the file system throws here and nowhere above it.
        try
        {
            using var stream = new FileStream(path, WriteOptions(FileOptions.None));
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(content);
            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(FileNotWritable(path, exception));
        }
    }

    /// <summary>Writes a file readable by the current account only, replacing any existing file.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="content">The text to write, encoded as UTF-8 without a byte-order mark.</param>
    /// <param name="cancellationToken">Token used to cancel the write.</param>
    public static async ValueTask<Result> WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        // This is the adapter boundary: the file system throws here and nowhere above it.
        try
        {
            var stream = new FileStream(path, WriteOptions(FileOptions.Asynchronous));
            await using (stream.ConfigureAwait(false))
            {
                var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                await using (writer.ConfigureAwait(false))
                {
                    await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
            }

            return Result.Success();
        }
        catch (OperationCanceledException canceled)
        {
            return Result.Failure(Error.Canceled(canceled));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(FileNotWritable(path, exception));
        }
    }

    // One description of an owner-only replacing write, so the two twins cannot drift apart.
    private static FileStreamOptions WriteOptions(FileOptions fileOptions)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = fileOptions,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = FileMode0600;

        return options;
    }

    /// <summary>The failure returned when a protected directory cannot be created.</summary>
    /// <param name="directory">The directory that could not be created.</param>
    /// <param name="exception">The underlying failure.</param>
    public static Error DirectoryNotCreatable(string directory, Exception exception) => Error.FromMessages(
        "The configuration directory could not be created.",
        $"configuration: creating directory '{directory}' failed: {exception.GetType().Name}: {exception.Message}",
        exception is UnauthorizedAccessException or PrivilegeNotHeldException ? ErrorCategory.Forbidden : ErrorCategory.Unexpected);

    /// <summary>The failure returned when a protected file cannot be written.</summary>
    /// <param name="path">The file that could not be written.</param>
    /// <param name="exception">The underlying failure.</param>
    public static Error FileNotWritable(string path, Exception exception) => Error.FromMessages(
        "The configuration could not be saved.",
        $"Writing '{path}' failed: {exception.GetType().Name}: {exception.Message}",
        exception is UnauthorizedAccessException ? ErrorCategory.Forbidden : ErrorCategory.Unexpected);

    [SupportedOSPlatform("windows")]
    private static void CreateWindows(string fullPath)
    {
        var me = WindowsIdentity.GetCurrent().User
            ?? throw new UnauthorizedAccessException("The current Windows identity has no security identifier.");

        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var security = new DirectorySecurity();
        // Protected: nothing inherits from the parent, so a volume-wide Authenticated Users grant
        // stops at this directory's boundary.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));

        FileSystemAclExtensions.Create(new DirectoryInfo(fullPath), security);
    }

    [UnsupportedOSPlatform("windows")]
    private static void CreateUnix(string fullPath) =>
        Directory.CreateDirectory(fullPath, DirectoryMode);
}
