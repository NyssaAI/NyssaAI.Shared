using System.Reflection;

using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Client;

/// <summary>
/// NyssaConfigurationOptions is everything the library takes at startup: who the application
/// is, where the workstation is, and — in <see cref="Bootstrap"/> — the defaults the developer
/// ships for what cannot come from the store because it is needed to open the store.
/// </summary>
/// <remarks>
/// Every value here has a usable default the moment the instance exists, so an application
/// registers with <c>AddNyssaConfiguration()</c> and sets nothing. There are two anchors, and they
/// move independently: <see cref="ConfigDirectory"/> is where this application's values live,
/// beneath the application's base directory; <see cref="BootstrapOptions.ConfigManifest"/> is the user's
/// manifest, beneath the profile directory. A test that wants an isolated workstation sets both.
/// </remarks>
public sealed class NyssaConfigurationOptions
{
    /// <summary>Creates options using the entry assembly's identity and conventional storage paths.</summary>
    public NyssaConfigurationOptions()
    {
        // A standard assembly title gives hosts an application identity without a custom attribute.
        var entryAssembly = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException(
                "configuration: there is no entry assembly to take the application name from; set ApplicationName explicitly");
        var title = entryAssembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title;
        ApplicationName = string.IsNullOrWhiteSpace(title) ? entryAssembly.GetName().Name! : title;

        // Keep application data beneath the application's base directory, independent of the user's manifest.
        ConfigDirectory = Path.Combine(AppContext.BaseDirectory, ".nyssa-ai", "config");
    }


    /// <summary>
    /// Gets or sets the application's identity. It names the manifest section and the keyring
    /// service the encryption key is filed under. The constructor takes it from the entry
    /// assembly's <see cref="System.Reflection.AssemblyTitleAttribute"/>, or from the assembly's
    /// own name when that attribute is absent or blank, so an application that sets nothing still
    /// has a name.
    /// </summary>
    public string ApplicationName { get; set; } = "";

    /// <summary>
    /// Gets or sets where this application's configuration lives: <c>&lt;app-base&gt;/.nyssa-ai/config</c> by
    /// default. It is what the library records when it provisions a manifest section for an
    /// application nobody has set up; once a section exists, the section's directory is the one
    /// that is opened, because the manifest gates which directory is consulted at all.
    /// </summary>
    public string ConfigDirectory { get; set; }

    /// <summary>Gets the developer's bootstrap defaults: what the library needs before it can open the store.</summary>
    public BootstrapOptions Bootstrap { get; } = new();
}

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

    /// <summary>Gets or sets the key vault this application reads sealed values through, or null when it uses none.</summary>
    public VaultOptions? KeyVault { get; set; }

}

/// <summary>
/// VaultOptions describes the key vault: the typed form of the <c>keyVault</c> entry in a
/// manifest section. <see cref="Name"/> is the provider name stored entries record, so changing it
/// orphans everything already sealed through the vault.
/// </summary>
public sealed class VaultOptions
{
    /// <summary>Gets or sets the provider name stored entries record.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the kind, matched against a registered <see cref="ICloudKeyVaultFactory.Kind"/>.</summary>
    public required string Kind { get; set; }

    /// <summary>Gets or sets the vault's endpoint, if the package needs one. Must be absolute.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the <c>namespace/key</c> of the entry this vault authenticates with, or null
    /// when the vault authenticates some other way. The entry is an ordinary sealed <c>oauth</c>
    /// entry in local storage, and may never itself be stored in this vault.
    /// </summary>
    public string? Credential { get; set; }

    /// <summary>Gets the package's own settings, as string pairs the library does not interpret.</summary>
    public IDictionary<string, string> Settings { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the package's own secrets, as string pairs the library does not interpret, log, or
    /// repeat. In code these are for tests; a deployed application keeps them in its manifest.
    /// </summary>
    public IDictionary<string, string> Secrets { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
