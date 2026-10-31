using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using NyssaAI.Results;

namespace NyssaAI.Configuration.FileSecurity;

/// <summary>
/// Refuses a directory or file that other accounts on the machine can write to, or that the
/// current account does not own.
/// </summary>
/// <remarks>
/// <para>
/// Configuration directories hold sealed secrets and the registry that decides which directory is
/// consulted at all. A directory a second account can write to lets that account swap a namespace
/// document, or repoint the registry at one it controls, and the reader has no way to notice: the
/// values it gets back are well-formed and correctly sealed, just not the ones the owner wrote.
/// The check therefore belongs before the read, not after it.
/// </para>
/// <para>
/// What is enforced differs by platform because the two express the same idea differently, and
/// what is <em>not</em> enforced is stated rather than implied:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <strong>Windows</strong> -- the owner must be the current user, and no access rule may grant
/// write to a well-known multi-user identity (Everyone, Authenticated Users, Users, Guests),
/// inherited rules included. Administrators and SYSTEM are permitted: an account that holds those
/// can take ownership regardless, so refusing them protects nothing and would fail on ordinary
/// machines. Because inherited rules count, directories must be created through
/// <see cref="ProtectedFileSystem"/>, which stops inheritance at the directory boundary.
/// </description></item>
/// <item><description>
/// <strong>Unix</strong> -- the mode must not grant write to group or other. Ownership is
/// <strong>not</strong> checked here: .NET exposes the mode through
/// <see cref="File.GetUnixFileMode(string)"/> but exposes no owner, and reading <c>st_uid</c> means
/// P/Invoking <c>stat</c>, whose symbol is glibc-version-dependent. That gap is real and is
/// recorded in the feature's open items rather than hidden; the mode check still refuses the case
/// that matters most, a directory another account can write to.
/// </description></item>
/// </list>
/// <para>
/// A directory or file that does not exist is not a failure here. Resolution creates it, and
/// creating it makes the current account its owner.
/// </para>
/// <para>
/// This type checks <em>who can write</em>, and for a file holding secrets, <em>who can read</em>.
/// </para>
/// </remarks>
internal static class DirectorySafety
{
    /// <summary>Checks that a directory is safe to read configuration from or write it to.</summary>
    /// <param name="directory">The directory to check.</param>
    /// <returns>Success when the directory is safe or does not exist yet.</returns>
    public static Result Verify(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Result.Success();
        }

        // This is the adapter boundary: the permission APIs throw for a directory that vanished
        // between the check above and the read below, and on a filesystem that cannot answer.
        try
        {
            return OperatingSystem.IsWindows()
                ? VerifyWindows(directory, FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(directory)))
                : VerifyUnix(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or PlatformNotSupportedException or PrivilegeNotHeldException)
        {
            return Result.Failure(NotVerifiable(directory, exception));
        }
    }

    /// <summary>
    /// Checks that a file is owned by the current account and cannot be written by other accounts.
    /// The rules are the same as <see cref="Verify"/>, applied to the file's own descriptor rather
    /// than its parent's.
    /// </summary>
    /// <param name="path">The file to check.</param>
    /// <returns>Success when the file is safe or does not exist yet.</returns>
    public static Result VerifyFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Result.Success();
        }

        // This is the adapter boundary: the permission APIs throw for a file that vanished between
        // the check above and the read below, and on a filesystem that cannot answer.
        try
        {
            return OperatingSystem.IsWindows()
                ? VerifyWindows(path, FileSystemAclExtensions.GetAccessControl(new FileInfo(path)))
                : VerifyUnix(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or PlatformNotSupportedException or PrivilegeNotHeldException)
        {
            return Result.Failure(NotVerifiable(path, exception));
        }
    }

    /// <summary>
    /// Checks that a file holding secrets is private: owned by the current account, and neither
    /// writable nor readable by other accounts. <see cref="VerifyFile"/> plus the read bits.
    /// </summary>
    /// <param name="path">The file to check.</param>
    /// <returns>Success when the file is private or does not exist yet; a failure that names the bit and the fix otherwise.</returns>
    public static Result VerifyPrivateFile(string path)
    {
        var writable = VerifyFile(path);
        if (writable.IsFailure)
            return writable;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Result.Success();

        // This is the adapter boundary: the permission APIs throw for a file that vanished between
        // the check above and the read below, and on a filesystem that cannot answer.
        try
        {
            return OperatingSystem.IsWindows()
                ? VerifyReadableWindows(path, FileSystemAclExtensions.GetAccessControl(new FileInfo(path)))
                : VerifyReadableUnix(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or PlatformNotSupportedException or PrivilegeNotHeldException)
        {
            return Result.Failure(NotVerifiable(path, exception));
        }
    }

    /// <summary>The failure returned when other accounts can read a file that holds secrets.</summary>
    /// <param name="path">The file that is too permissive.</param>
    /// <param name="detail">Which identity or mode bit grants the read, and who that includes.</param>
    /// <param name="fix">The exact command that makes the file private.</param>
    public static Error Exposed(string path, string detail, string fix) => Error.FromMessages(
        "The configuration manifest holds secrets that other accounts on this machine can read.",
        $"manifest: '{path}' is readable by {detail}; it carries vault secrets, so startup is refused until it is private — fix with: {fix}",
        ErrorCategory.Forbidden);

    /// <summary>The failure returned when other accounts can write to the directory or file.</summary>
    /// <param name="path">The directory or file that is too permissive.</param>
    /// <param name="detail">Which identity or mode bit grants the access.</param>
    public static Error Writable(string path, string detail) => Error.FromMessages(
        "The configuration location can be changed by other accounts on this machine.",
        $"configuration: '{path}' is writable by {detail}",
        ErrorCategory.Forbidden);

    /// <summary>The failure returned when the current account does not own the directory or file.</summary>
    /// <param name="path">The directory or file that is owned by someone else.</param>
    /// <param name="owner">The owner the filesystem reports.</param>
    public static Error NotOwned(string path, string owner) => Error.FromMessages(
        "The configuration location belongs to a different account.",
        $"configuration: '{path}' is owned by {owner}, not the current user",
        ErrorCategory.Forbidden);

    /// <summary>The failure returned when the permissions could not be read.</summary>
    /// <param name="path">The directory or file that could not be inspected.</param>
    /// <param name="exception">Why the inspection failed.</param>
    /// <remarks>
    /// Failing closed. Being unable to establish that a secret store is private is not the same as
    /// establishing that it is.
    /// </remarks>
    public static Error NotVerifiable(string path, Exception exception) => Error.FromMessages(
        "The configuration location's permissions could not be checked.",
        $"configuration: could not read permissions on '{path}': {exception.GetType().Name}: {exception.Message}",
        ErrorCategory.Forbidden);

    [SupportedOSPlatform("windows")]
    private static Result VerifyWindows(string path, FileSystemSecurity security)
    {
        // Write, append, and the two that let a holder grant themselves write later.
        const FileSystemRights writeRights =
            FileSystemRights.WriteData
            | FileSystemRights.AppendData
            | FileSystemRights.ChangePermissions
            | FileSystemRights.TakeOwnership;

        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        var me = WindowsIdentity.GetCurrent().User;

        if (owner is null)
        {
            return Result.Failure(
                NotOwned(path, "an owner the filesystem did not report"));
        }

        if (me is not null && !owner.Equals(me) && !IsTrustedAdministrativeIdentity(owner))
        {
            return Result.Failure(NotOwned(path, owner.Value));
        }

        foreach (FileSystemAccessRule rule in
                 security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            if ((rule.FileSystemRights & writeRights) == 0)
            {
                continue;
            }

            if (rule.IdentityReference is SecurityIdentifier identity && IsMultiUserIdentity(identity))
            {
                return Result.Failure(Writable(path, identity.Value));
            }
        }

        return Result.Success();
    }

    [UnsupportedOSPlatform("windows")]
    private static Result VerifyUnix(string path)
    {
        var mode = File.GetUnixFileMode(path);

        if ((mode & UnixFileMode.OtherWrite) != 0)
        {
            return Result.Failure(Writable(path, "other (o+w)"));
        }

        return (mode & UnixFileMode.GroupWrite) != 0
            ? Result.Failure(Writable(path, "group (g+w)"))
            : Result.Success();
    }

    [SupportedOSPlatform("windows")]
    private static Result VerifyReadableWindows(string path, FileSystemSecurity security)
    {
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow || (rule.FileSystemRights & FileSystemRights.ReadData) == 0)
                continue;

            if (rule.IdentityReference is SecurityIdentifier identity && IsMultiUserIdentity(identity))
            {
                return Result.Failure(Exposed(
                    path,
                    $"{identity.Value} (every account in that group)",
                    $"icacls \"{path}\" /inheritance:r /grant:r \"%USERNAME%:F\""));
            }
        }

        return Result.Success();
    }

    [UnsupportedOSPlatform("windows")]
    private static Result VerifyReadableUnix(string path)
    {
        var mode = File.GetUnixFileMode(path);
        var fix = $"chmod 600 '{path}'";

        if ((mode & (UnixFileMode.OtherRead | UnixFileMode.OtherExecute)) != 0)
            return Result.Failure(Exposed(path, "other (o+r): every account on this machine", fix));

        return (mode & (UnixFileMode.GroupRead | UnixFileMode.GroupExecute)) != 0
            ? Result.Failure(Exposed(path, "group (g+r): every member of the file's group", fix))
            : Result.Success();
    }

    [SupportedOSPlatform("windows")]
    private static bool IsMultiUserIdentity(SecurityIdentifier identity) =>
        identity.IsWellKnown(WellKnownSidType.WorldSid)
        || identity.IsWellKnown(WellKnownSidType.AuthenticatedUserSid)
        || identity.IsWellKnown(WellKnownSidType.BuiltinUsersSid)
        || identity.IsWellKnown(WellKnownSidType.BuiltinGuestsSid);

    // An account holding either of these can take ownership of anything on the machine, so treating
    // its ownership as a violation would refuse ordinary directories while protecting nothing.
    [SupportedOSPlatform("windows")]
    private static bool IsTrustedAdministrativeIdentity(SecurityIdentifier identity) =>
        identity.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
        || identity.IsWellKnown(WellKnownSidType.LocalSystemSid);
}
