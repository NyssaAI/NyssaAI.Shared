using System.Security.AccessControl;
using System.Security.Principal;

namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>
/// The one place these tests are allowed to write: <c>&lt;repository&gt;/.temp</c>.
/// </summary>
/// <remarks>
/// A tutorial demonstrates an application that names no paths, and the paths such an application
/// gets are the user's own — <c>~/.nyssa-ai/config-manifest.json</c> above all. A test suite may not
/// write there: the file is the developer's, it may list their real applications, and a test run
/// is not allowed to create, edit, or remove it. So every home a tutorial uses is a folder under
/// this root, inside the repository, thrown away when the tutorial is done.
/// </remarks>
internal static class RepositoryTemp
{
    private static readonly string Root = Locate();

    /// <summary>Creates a private folder under the temp root, to be used as one tutorial's home directory.</summary>
    /// <param name="purpose">A short name for what the folder holds, so a stray one says where it came from.</param>
    public static DirectoryInfo CreateHome(string purpose)
    {
        var home = Directory.CreateDirectory(Path.Combine(Root, $"{purpose}-{Guid.NewGuid():N}"));
        // A checkout may inherit group-write access. A simulated private home must not carry it
        // into manifests, whose production reader correctly rejects writable-by-others files.
        if (OperatingSystem.IsWindows())
        {
            var owner = WindowsIdentity.GetCurrent().User!;
            var permissions = new DirectorySecurity();

            permissions.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            permissions.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            home.SetAccessControl(permissions);
        }
        return home;
    }

    /// <summary>
    /// Deletes one folder this class handed out, and then the root itself once nothing is left in
    /// it. Only the caller's own folder is removed: test classes run in parallel, and each one's
    /// homes are live until its own last tutorial is done.
    /// </summary>
    /// <param name="home">The folder <see cref="CreateHome"/> returned.</param>
    public static void DeleteHome(DirectoryInfo home)
    {
        ArgumentNullException.ThrowIfNull(home);

        if (home.Exists)
            home.Delete(recursive: true);

        // Best effort, and only when empty: another class may be mid-run in a folder of its own.
        try
        {
            if (Directory.Exists(Root) && !Directory.EnumerateFileSystemEntries(Root).Any())
                Directory.Delete(Root);
        }
        catch (IOException)
        {
            // Someone created a folder between the check and the delete. It is their cleanup now.
        }
    }

    // Tutorials live under the repository's test/ directory, alongside the package directories.
    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NyssaAI.Shared.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("the repository root could not be found above " + AppContext.BaseDirectory);

        return Path.Combine(directory.FullName, ".temp");
    }
}
