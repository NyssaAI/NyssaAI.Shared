using System.Reflection;

namespace NyssaAI.Configuration.Client;

/// <summary>
/// NyssaConfigurationOptions is everything the library takes at startup: who the application
/// is, where the workstation is, and — in <see cref="Bootstrap"/> — the defaults the developer
/// ships for what cannot come from the store because it is needed to open the store.
/// </summary>
/// <remarks>
/// Every value here has a usable default the moment the instance exists, so an application
/// registers with <c>AddNyssaConfiguration()</c> and sets nothing. By default,
/// <see cref="ConfigDirectory"/> is <c>~/.nyssa-ai/&lt;ApplicationName&gt;</c> (beneath the directory of
/// <see cref="BootstrapOptions.ConfigManifest"/>), and <see cref="BootstrapOptions.ConfigManifest"/> is the user's
/// manifest beneath the profile directory. A test that wants an isolated workstation sets both.
/// </remarks>
public sealed class NyssaConfigurationOptions
{
    private string? _configDirectory;

    /// <summary>Creates options using the entry assembly's identity and conventional storage paths.</summary>
    public NyssaConfigurationOptions()
    {
        // A standard assembly title gives hosts an application identity without a custom attribute.
        var entryAssembly = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException(
                "configuration: there is no entry assembly to take the application name from; set ApplicationName explicitly");
        var title = entryAssembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title;
        ApplicationName = string.IsNullOrWhiteSpace(title) ? entryAssembly.GetName().Name! : title;
    }

    /// <summary>
    /// Gets or sets the application's identity. It names the manifest section and the keyring
    /// entry the encryption key is filed under. The constructor takes it from the entry
    /// assembly's <see cref="System.Reflection.AssemblyTitleAttribute"/>, or from the assembly's
    /// own name when that attribute is absent or blank, so an application that sets nothing still
    /// has a name.
    /// </summary>
    public string ApplicationName { get; set; } = "";

    /// <summary>
    /// Gets or sets where this application's configuration lives: <c>~/.nyssa-ai/&lt;ApplicationName&gt;</c> by
    /// default (beneath the folder of <see cref="BootstrapOptions.ConfigManifest"/>). It is what the library records when it provisions a manifest section for an
    /// application nobody has set up; once a section exists, the section's directory is the one
    /// that is opened, because the manifest gates which directory is consulted at all.
    /// </summary>
    public string ConfigDirectory
    {
        get => _configDirectory ?? Path.Combine(
            Path.GetDirectoryName(Bootstrap.ConfigManifest)
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), BootstrapOptions.ManifestFolderName),
            ApplicationName);
        set => _configDirectory = value;
    }

    /// <summary>Gets the developer's bootstrap defaults: what the library needs before it can open the store.</summary>
    public BootstrapOptions Bootstrap { get; } = new();
}
