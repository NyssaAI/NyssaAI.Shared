using NyssaAI.Configuration.Client;

namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>
/// Creates the workstation a tutorial starts from. One method per starting condition, named for
/// what has — or has not — been done to the machine before the application runs, so a tutorial's
/// arrange step is one line and the condition it depends on is the method it called.
/// </summary>
/// <remarks>
/// The factory owns nothing a <see cref="Workstation"/> owns: every one it hands back is the
/// caller's to dispose, and disposing one takes its home directory with it. What the factory owns
/// is <see cref="UserManifestPath"/> — the stand-in for the user's own manifest, which a tutorial
/// about an application that names nothing still must not write to for real — and the cleanup that
/// removes it. See <see cref="RepositoryTemp"/> for why nothing here goes near <c>~/.nyssa-ai</c>.
/// </remarks>
public sealed class WorkstationFactory : IDisposable
{
    private readonly DirectoryInfo _home = RepositoryTemp.CreateHome("user");

    /// <summary>
    /// Stands in for <c>~/.nyssa-ai/config-manifest.json</c>: the manifest an application that names
    /// no path is given. It is the conventional <c>&lt;home&gt;/.nyssa-ai/config-manifest.json</c>
    /// beneath a home of this factory's own, so the library treats it exactly as it treats the
    /// user's — including creating it when it is absent.
    /// </summary>
    public string UserManifestPath => BootstrapOptions.ManifestPathFor(_home.FullName);

    /// <summary>
    /// A machine and an application where nothing has been set up: no manifest at all, no
    /// configuration directory, a reachable but empty credential store, and no vault package
    /// referenced. The only answer that exists is the application's name, which is compiled into
    /// the entry assembly and cannot be taken away.
    /// </summary>
    public Workstation CreateBlankWorkstation()
    {
        // A fresh Workstation carries the manifest an init run, or an operator, leaves behind.
        // Blank is the same machine before either happened.
        var workstation = new Workstation();
        workstation.DeleteManifest();
        return workstation;
    }

    /// <summary>
    /// Removes what the tutorials wrote: this factory's stand-in for the user's manifest, and the
    /// default configuration directory a tutorial that named no directory was given. Every
    /// <see cref="Workstation"/> removes its own home when it is disposed.
    /// </summary>
    public void CleanUp()
    {
        RepositoryTemp.DeleteHome(_home);

        // ConfigDirectory is the one default a tutorial may safely take, because it is <cwd>/.nyssa-ai/config
        // and the working directory of a test run is the test's own output folder.
        var defaults = new NyssaConfigurationOptions();
        if (Directory.Exists(defaults.ConfigDirectory))
            Directory.Delete(defaults.ConfigDirectory, recursive: true);
    }

    /// <summary>Cleans up once the class's tutorials have all run.</summary>
    public void Dispose() => CleanUp();
}
