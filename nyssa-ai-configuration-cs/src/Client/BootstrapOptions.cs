namespace NyssaAI.Configuration.Client;

/// <summary>
/// BootstrapOptions is the developer's deterministic defaults for what the library needs before
/// it can open the store — the same shape an operator writes into a section of
/// <c>~/.nyssa-ai/config-manifest.json</c>. At minimum, the key vault.
/// </summary>
/// <remarks>
/// These are defaults, so the same binary starts the same way on every machine. A manifest
/// section is layered over them: a manifest key vault replaces <see cref="KeyVault"/> whole,
/// because an operator who names a vault owns everything about it, and a manifest lock timeout
/// replaces <see cref="LockTimeout"/>.
/// The configuration directory is not here: it comes from the manifest section, at the default
/// path or the one <see cref="ConfigManifest"/> names. Encryption is absent because it is never
/// optional.
/// </remarks>
public sealed class BootstrapOptions
{
    /// <summary>The file name for configuration manifests.</summary>
    public const string ManifestFileName = "config-manifest.json";

    /// <summary>The conventional folder a manifest sits in, beneath a home directory.</summary>
    public const string ManifestFolderName = ".nyssa-ai";

    /// <summary>The user's own manifest: <c>~/.nyssa-ai/config-manifest.json</c>.</summary>
    public static string DefaultConfigManifest { get; } = ManifestPathFor(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>
    /// Sets safe defaults for the configuration options.
    /// </summary>
    public BootstrapOptions()
    {
        // set the default ConfigManifest full path to ~/.nyssa-ai/{ManifestFileName}
        ConfigManifest = DefaultConfigManifest;
    }

    /// <summary>The conventional manifest path beneath one home directory: <c>&lt;home&gt;/.nyssa-ai/config-manifest.json</c>.</summary>
    /// <param name="homeDirectory">The home directory the manifest sits beneath.</param>
    public static string ManifestPathFor(string homeDirectory) =>
        Path.Combine(homeDirectory, ManifestFolderName, ManifestFileName);

    /// <summary>
    /// Gets or sets where the manifest is. The user's own by default; an application that ships
    /// one — a service deployed with its manifest beside it — names that path instead. Same schema
    /// either way. A missing manifest or application section is created at this path on first use.
    /// </summary>
    public string ConfigManifest { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bounded wait for a document lock. It has to outlast real contention: the
    /// lock retries with a backoff that starts at 5ms, so a timeout shorter than that fails every
    /// reader that arrives second.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets the primary key vault this application reads sealed values through, or null when it uses none.</summary>
    public VaultOptions? KeyVault { get; set; }

    /// <summary>
    /// Gets additional key vaults this application reads sealed values through. Distinct vault
    /// names coexist alongside <see cref="KeyVault"/> and root <c>keyVaults</c> entries in the
    /// configuration manifest; a manifest vault with the same <see cref="VaultOptions.Name"/>
    /// replaces the programmatic entry whole.
    /// </summary>
    public IList<VaultOptions> KeyVaults { get; } = [];
}
