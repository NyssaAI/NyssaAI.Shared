using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.UseCaseTests.domain;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>
/// How SomeApp-cli bootstraps NyssaAI.Configuration, as one story. Bootstrap is the three things
/// the library needs before it can open the store — and that therefore cannot come from the
/// store: who the application is, where its configuration directory is, and which secret manager
/// it uses. Each is answered in one place, and each answer is deterministic: the same binary
/// starts the same way on every machine, or says exactly why it cannot.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who.</b> The application declares its name in its project file, once:
/// <c>&lt;AssemblyTitle&gt;SomeApp-cli&lt;/AssemblyTitle&gt;</c>, which the library reads off the
/// entry assembly. The name locates everything else — the manifest section, the keyring service,
/// the default directory — so it is never read from the environment. An application that declares
/// no title falls back to the entry assembly's own name.
/// </para>
/// <para>
/// <b>Where.</b> The user's manifest, <c>~/.nyssa-ai/config-manifest.json</c>, has one section per
/// application naming its configuration directory. The application's <c>init</c> command writes
/// that section; an operator may write the rest. A service ships the same schema beside itself
/// and names the path in code.
/// </para>
/// <para>
/// <b>Which.</b> Vault packages are compiled in and register a factory by <c>kind</c>; the
/// section — or the developer's defaults in code — names which kinds run and how they
/// authenticate. Nothing is loaded by name, which is what keeps it honest under Native AOT.
/// </para>
/// <para>
/// The registration takes the same options in three shapes — nothing, an instance, or a delegate
/// that edits one — and they funnel into one call. The <see cref="Workstation"/> fixture uses the
/// delegate form; the other two are the same values written differently.
/// </para>
/// </remarks>
public sealed class ManifestTutorials : IClassFixture<WorkstationFactory>
{
    private readonly WorkstationFactory _hostFactory;

    /// <summary>Takes the factory that creates the workstation each tutorial starts from.</summary>
    /// <param name="hostFactory">Creates a workstation in one named starting condition.</param>
    public ManifestTutorials(WorkstationFactory hostFactory) => _hostFactory = hostFactory;

    /// <summary>
    /// This simulates a system where there is an application with the NyssaAI.Configuration library in use but the application
    /// developer did nothing to setup configuration at all.
    /// </summary>
    [Fact]
    public async Task AddNyssaConfiguration_BlankWorkstation_CreatesManifestAndEncryptedSettingsAsync()
    {
        // Arrange
        using (var workstation = _hostFactory.CreateBlankWorkstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            // Program.cs, in full — the generic host an application really builds. The default
            // registration will generate ~/.nyssa-ai/config-manifest.json, load the AES crypto provider
            // for secrets and use the OsKeychain to hold the crypt key it creates
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                workstation.Locate(options);
                options.Bootstrap.ConfigManifest = _hostFactory.UserManifestPath;
            });
            workstation.StartApplication(services);
            IConfiguration configManager = workstation.Host.GetRequiredService<IConfiguration>();
            NyssaConfigurationOptions options = workstation.Host.GetRequiredService<NyssaConfigurationOptions>();
            // a secret, and an object
            await configManager.SetAsync(
                "mail",
                "password",
                "fictional-password",
                provider: "aesLocal",
                cancellationToken: cancellationToken
            );
            await configManager.SetAsAsync(
                "mail",
                "preferences",
                new ExportPreferences { Format = "csv", IncludeHistory = true },
                "aesLocal",
                cancellationToken
            );
            // the one file this application's values were written into
            var mailFile = ConfigurationFile(options, "mail");

            // Assert
            Assert.Equal(Workstation.ApplicationName, options.ApplicationName); // <AssemblyTitle> in this .csproj; the assembly name is only the fallback
            Assert.True(Path.Exists(options.Bootstrap.ConfigManifest)); // the manifest was created
            Assert.Equal(_hostFactory.UserManifestPath, options.Bootstrap.ConfigManifest); // the manifest is in this location: the stand-in for ~/.nyssa-ai/config-manifest.json
            Assert.NotEqual(BootstrapOptions.DefaultConfigManifest, options.Bootstrap.ConfigManifest); // and never the developer's real one
            Assert.Equal(options.ConfigDirectory, DirectoryNamedInManifest(options)); // the manifest names the directory the options supplied
            Assert.True(Directory.Exists(options.ConfigDirectory)); // the configuration directory it supplied exists
            Assert.Equal(Path.Combine(options.ConfigDirectory, "mail.json"), mailFile); // the test configuration file is where it is supposed to be
            Assert.True(File.Exists(mailFile)); // and it is there
            Assert.True(IsSealed(mailFile, "password")); // the test configuration has an encrypted value
            Assert.DoesNotContain("fictional-password", StoredText(mailFile, "password"), StringComparison.Ordinal); // sealed means sealed: no plaintext on disk
            Assert.Equal("aesLocal", SealedBy(mailFile, "password")); // the expected clear value beside it: which provider sealed the entry
        }
    }

    /// <summary>
    /// A service deployed with its manifest beside it: the application names where the manifest is
    /// and nothing else. The manifest sits at the conventional <c>.nyssa-ai/config-manifest.json</c>,
    /// so a machine that has never run this application still gets a section written for it.
    /// </summary>
    [Fact]
    public async Task AddNyssaConfiguration_ChosenManifest_CreatesEncryptedSettingsAsync()
    {
        // Arrange
        using (var workstation = _hostFactory.CreateBlankWorkstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            // Program.cs: the two paths this deployment owns, and nothing else
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                workstation.Locate(options);
                options.Bootstrap.ConfigManifest = workstation.ManifestPath;
                options.ConfigDirectory = workstation.ConfigurationDirectory;
            });
            workstation.StartApplication(services);
            IConfiguration configManager = workstation.Host.GetRequiredService<IConfiguration>();
            NyssaConfigurationOptions options = workstation.Host.GetRequiredService<NyssaConfigurationOptions>();
            await configManager.SetAsync(
                "mail",
                "password",
                "fictional-password",
                provider: "aesLocal",
                cancellationToken: cancellationToken
            );
            // the one file this application's values were written into
            var mailFile = ConfigurationFile(options, "mail");

            // Assert
            Assert.Equal(workstation.ManifestPath, options.Bootstrap.ConfigManifest); // the manifest is where the application said
            Assert.NotEqual(BootstrapOptions.DefaultConfigManifest, options.Bootstrap.ConfigManifest); // and not at the user's default
            Assert.True(File.Exists(options.Bootstrap.ConfigManifest)); // there was no manifest; the library created it
            Assert.Equal(options.ConfigDirectory, DirectoryNamedInManifest(options)); // the section it wrote names the directory the options supplied
            Assert.True(Directory.Exists(options.ConfigDirectory)); // the configuration directory exists
            Assert.True(File.Exists(mailFile)); // the test configuration file is where it is supposed to be
            Assert.True(IsSealed(mailFile, "password")); // the test configuration has an encrypted value
            Assert.Equal("aesLocal", SealedBy(mailFile, "password")); // the expected clear value beside it: which provider sealed the entry
        }
    }

    /// <summary>
    /// The manifest gates the directory. An operator placed one naming where this application's
    /// configuration lives, so that is where the store opens — the directory the developer
    /// defaulted to in code is not consulted and is never created.
    /// </summary>
    [Fact]
    public async Task AddNyssaConfiguration_ExistingManifest_OverridesDirectoryDefaultAsync()
    {
        // Arrange
        using (var workstation = _hostFactory.CreateBlankWorkstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var operatorDirectory = Path.Combine(workstation.Home, "from-operator");
            var developerDirectory = Path.Combine(workstation.Home, "from-code");
            Workstation.WriteManifest(workstation.ManifestPath, ManifestNaming(operatorDirectory));

            // Act
            // Program.cs: the developer's own idea of where configuration goes, which the manifest overrides
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                workstation.Locate(options);
                options.Bootstrap.ConfigManifest = workstation.ManifestPath;
                options.ConfigDirectory = developerDirectory;
            });
            workstation.StartApplication(services);
            IConfiguration configManager = workstation.Host.GetRequiredService<IConfiguration>();
            NyssaConfigurationOptions options = workstation.Host.GetRequiredService<NyssaConfigurationOptions>();
            await configManager.SetAsync(
                "mail",
                "password",
                "fictional-password",
                provider: "aesLocal",
                cancellationToken: cancellationToken
            );
            // the file the operator's directory should hold, and the one the developer's should not
            var operatorFile = ConfigurationFile(operatorDirectory, "mail");
            var developerFile = ConfigurationFile(developerDirectory, "mail");

            // Assert
            Assert.NotEqual(operatorDirectory, developerDirectory); // the two answers really are different
            Assert.Equal(operatorDirectory, DirectoryNamedInManifest(options)); // the manifest still names what the operator wrote
            Assert.Equal(developerDirectory, options.ConfigDirectory); // the options still carry what the developer wrote
            Assert.True(File.Exists(operatorFile)); // the store opened in the manifest's directory
            Assert.False(Directory.Exists(developerDirectory)); // the developer's directory was never created
            Assert.False(File.Exists(developerFile)); // and nothing was written into it
            Assert.True(IsSealed(operatorFile, "password")); // the test configuration has an encrypted value
            Assert.Equal("aesLocal", SealedBy(operatorFile, "password")); // the expected clear value beside it: which provider sealed the entry
        }
    }

    /// <summary>The caller chooses an isolated home and plugin settings directory using the options callback.</summary>
    [Fact]
    public async Task AddNyssaConfiguration_CustomHomeAndPluginDirectory_PersistsSettingsAtChosenPathsAsync()
    {
        // Arrange
        using (var workstation = _hostFactory.CreateBlankWorkstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var chosenHome = Path.Join(workstation.Home, "plugin-user");
            var pluginDirectory = Path.Join(chosenHome, "plugins", "report-export", "settings");

            // Act
            // ConfigDirectory supplies the directory for a newly created manifest section.
            // ConfigManifest anchors that section in the chosen home rather than the real user's profile.
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                workstation.Locate(options);
                options.ConfigDirectory = pluginDirectory;
                options.Bootstrap.ConfigManifest = Path.Join(chosenHome, ".nyssa-ai", "config-manifest.json");
            });
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            // "plugin" is the namespace file; "name" is the entry. Plain storage is chosen for this nonsecret label.
            await configuration.SetAsync("plugin", "name", "report-export", cancellationToken: cancellationToken);
            var name = await configuration.GetAsync("plugin", "name", cancellationToken);

            // Assert
            Assert.Equal("report-export", name);
            Assert.True(File.Exists(Path.Join(chosenHome, ".nyssa-ai", "config-manifest.json")));
            Assert.True(File.Exists(Path.Join(pluginDirectory, "plugin.json")));
            Assert.Contains(
                "report-export",
                File.ReadAllText(Path.Join(pluginDirectory, "plugin.json")),
                StringComparison.Ordinal
            );
            Assert.False(File.Exists(workstation.ManifestPath));
            Assert.False(Directory.Exists(workstation.ConfigurationDirectory));
        }
    }

    #region static test helpers
    /// <summary>A manifest with one section for this application, naming one directory.</summary>
    /// <param name="configurationDirectory">The directory the section names.</param>
    private static string ManifestNaming(string configurationDirectory) =>
        $$"""
            {
              "version": 1,
              "applications": {
                "{{Workstation.ApplicationName}}": { "configurationDirectory": {{JsonSerializer.Serialize(
                configurationDirectory
            )}} }
              }
            }
            """;

    /// <summary>Where a namespace's values are kept: one file per namespace, in the configuration directory.</summary>
    /// <param name="options">The startup options the host resolved.</param>
    /// <param name="namespace">The namespace whose file is wanted.</param>
    private static string ConfigurationFile(NyssaConfigurationOptions options, string @namespace) =>
        ConfigurationFile(options.ConfigDirectory, @namespace);

    /// <summary>Where a namespace's values are kept, for a directory the options do not name.</summary>
    /// <param name="configurationDirectory">The directory the store opened in.</param>
    /// <param name="namespace">The namespace whose file is wanted.</param>
    private static string ConfigurationFile(string configurationDirectory, string @namespace) =>
        Path.Combine(configurationDirectory, $"{@namespace}.json");

    /// <summary>The configuration directory this application's manifest section names.</summary>
    /// <param name="options">The startup options the host resolved.</param>
    private static string DirectoryNamedInManifest(NyssaConfigurationOptions options)
    {
        using (var manifest = JsonDocument.Parse(File.ReadAllText(options.Bootstrap.ConfigManifest)))
        {
            return manifest
                .RootElement.GetProperty("applications")
                .GetProperty(options.ApplicationName)
                .GetProperty("configurationDirectory")
                .GetString()!;
        }
    }

    /// <summary>Whether the entry on disk is sealed.</summary>
    /// <param name="configurationFile">The namespace file to read.</param>
    /// <param name="key">The entry within it.</param>
    private static bool IsSealed(string configurationFile, string key) =>
        Entry(configurationFile, key).GetProperty("encrypted").GetBoolean();

    /// <summary>Which provider sealed the entry, recorded beside it in the clear.</summary>
    /// <param name="configurationFile">The namespace file to read.</param>
    /// <param name="key">The entry within it.</param>
    private static string SealedBy(string configurationFile, string key) =>
        Entry(configurationFile, key).GetProperty("provider").GetString()!;

    /// <summary>The entry's value exactly as the file holds it — ciphertext, when it is sealed.</summary>
    /// <param name="configurationFile">The namespace file to read.</param>
    /// <param name="key">The entry within it.</param>
    private static string StoredText(string configurationFile, string key) =>
        Entry(configurationFile, key).GetProperty("value").GetString()!;

    // One entry out of a namespace file. Cloned, so it outlives the document it was read from.
    private static JsonElement Entry(string configurationFile, string key)
    {
        using (var document = JsonDocument.Parse(File.ReadAllText(configurationFile)))
        {
            return document.RootElement.GetProperty(key).Clone();
        }
    }
    #endregion
}
