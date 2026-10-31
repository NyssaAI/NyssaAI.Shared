using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.UseCaseTests.domain;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>
/// Setting up the configuration system for SomeApp-cli. Read this file first: it is everything
/// that has to be true before a class can take <see cref="IConfiguration"/> in its constructor.
/// UC-001, UC-002, UC-010, UC-011, UC-017, UC-018.
/// </summary>
/// <remarks>
/// The application declares its name once — <c>&lt;AssemblyTitle&gt;SomeApp-cli&lt;/AssemblyTitle&gt;</c>
/// in its project file — and writes one line, <c>services.AddNyssaConfiguration()</c>. What that
/// line finds depends on the machine: its section in the user's manifest at
/// <c>~/.nyssa-ai/config-manifest.json</c>, written by <c>init</c>, by an operator, or by the library
/// itself on first run, names the configuration directory and the key vault. The manifest gates
/// which directory is opened at all. The <see cref="Workstation"/> fixture is that machine, with the OS replaced
/// by in-memory stand-ins registered through the framework's own types.
/// </remarks>
public sealed class SetupTutorials
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>UC-001. One registration, nothing else named. The manifest section from a previous init
    /// names the directory; the library provisions its own encryption key, says so in the log, and runs.
    /// A class that needs settings takes IConfiguration in its constructor — see MailService — and never
    /// learns where the directory is, what the key is, or which providers exist.
    /// </summary>
    [Fact]
    public async Task AddNyssaConfiguration_PreparedWorkstation_InjectsEncryptedConfigurationAsync()
    {
        // Arrange
        // A workstation init has run on: a manifest section, a reachable credential store holding nothing
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var manifestExisted = File.Exists(workstation.ManifestPath);

            // Act
            // then the application's own classes, which depend on IConfiguration and nothing Nyssa
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            services.AddScoped<MailService>();
            workstation.StartApplication(services);
            var mail = workstation.Host.GetRequiredService<MailService>();
            await mail.RememberPasswordAsync("fictional-password", cancellationToken);
            var password = await mail.PasswordAsync(cancellationToken);

            // Assert
            Assert.True(manifestExisted);
            Assert.Equal("fictional-password", password);
            // The library created the key itself, once, at the address the application name implies, and logged it
            Assert.True(workstation.Keyring.Holds(ConfigurationServiceCollectionExtensions.KeyringService, Workstation.ApplicationName));
            Assert.Equal([(ConfigurationServiceCollectionExtensions.KeyringService, Workstation.ApplicationName)], workstation.Keyring.Writes);
            Assert.Contains(
                workstation.Log.Messages,
                message =>
                    message.Contains("created encryption key", StringComparison.OrdinalIgnoreCase)
                    && message.Contains(Workstation.ApplicationName, StringComparison.Ordinal)
            );
            // and the secret is sealed on disk with it, in the directory the manifest section names
            Assert.DoesNotContain("fictional-password", workstation.ReadDocument("mail"), StringComparison.Ordinal);
            Assert.Equal(
                Path.Combine(workstation.Home, ".nyssa-ai", "SomeApp-cli"),
                workstation.ConfigurationDirectory
            );
        }
    }

    /// <summary>UC-001. The entry assembly names the application, so an application that sets nothing
    /// still has an identity: its AssemblyTitle is the section it reads, the directory it opens, and the
    /// keyring service its key is filed under.
    /// </summary>
    [Fact]
    public async Task AddNyssaConfiguration_DefaultName_UsesAssemblyTitleAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            // Program.cs: services.AddNyssaConfiguration()
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync(
                "mail",
                "password",
                "fictional-password",
                provider: "aesLocal",
                cancellationToken: cancellationToken
            );

            // Assert
            // Nobody wrote the name: <AssemblyTitle>SomeApp-cli</AssemblyTitle> found the section, the directory, and the key
            Assert.Equal(
                Path.Combine(workstation.Home, ".nyssa-ai", "SomeApp-cli"),
                workstation.ConfigurationDirectory
            );
            Assert.True(workstation.Keyring.Holds(ConfigurationServiceCollectionExtensions.KeyringService, Workstation.ApplicationName));
        }
    }

    /// <summary>UC-001, variation. The registration call is the last word: an ApplicationName set there
    /// overrides the name the constructor took from the entry assembly, and every address moves with it —
    /// the manifest section, the directory, and the keyring service.
    /// </summary>
    [Fact]
    public async Task AddNyssaConfiguration_ExplicitName_OverridesAssemblyTitleAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            // Program.cs: services.AddNyssaConfiguration(options => options.ApplicationName = "someone-else")
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                workstation.Locate(options);
                options.ApplicationName = "someone-else";
            });
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync(
                "mail",
                "password",
                "fictional-password",
                provider: "aesLocal",
                cancellationToken: cancellationToken
            );

            // Assert
            // Every address moved: "someone-else" got its own section, and its own key
            Assert.Contains("someone-else", File.ReadAllText(workstation.ManifestPath), StringComparison.Ordinal);
            Assert.True(workstation.Keyring.Holds(ConfigurationServiceCollectionExtensions.KeyringService, "someone-else"));
            Assert.False(workstation.Keyring.Holds(ConfigurationServiceCollectionExtensions.KeyringService, Workstation.ApplicationName));
        }
    }

    /// <summary>UC-001, failure case. No credential store is a machine that cannot seal anything. The
    /// library says so at resolution instead of quietly running without encryption.
    /// </summary>
    [Fact]
    public void AddNyssaConfiguration_UnreachableCredentialStore_ThrowsAtResolution()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            workstation.Keyring.Unreachable = true;

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            var failure = Assert.Throws<InvalidOperationException>(() =>
                workstation.Host.GetRequiredService<IConfiguration>()
            );

            // Assert

            // The message names the address it needed, so an operator knows what to provision
            Assert.Contains(Workstation.ApplicationName, failure.Message, StringComparison.Ordinal);
            Assert.Contains(ConfigurationServiceCollectionExtensions.KeyringService, failure.Message, StringComparison.Ordinal);
            Assert.False(workstation.DocumentExists("mail"));
        }
    }

    /// <summary>UC-001. A machine nobody has prepared has no manifest. The library writes the default
    /// section for the application rather than asking to be told: referencing the package and registering
    /// it is the whole of what a developer does, and the first run leaves the machine configured.
    /// </summary>
    [Fact]
    public async Task AddNyssaConfiguration_MissingManifest_ProvisionsWorkstationAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            workstation.DeleteManifest();

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync(
                "mail",
                "password",
                "fictional-password",
                provider: "aesLocal",
                cancellationToken: cancellationToken
            );

            // Assert
            // The manifest the machine lacked, naming the directory the application's name implies
            Assert.True(File.Exists(workstation.ManifestPath));
            Assert.Contains(
                Workstation.ApplicationName,
                File.ReadAllText(workstation.ManifestPath),
                StringComparison.Ordinal
            );
            Assert.Equal(
                Path.Combine(workstation.Home, ".nyssa-ai", Workstation.ApplicationName),
                workstation.ConfigurationDirectory
            );
            Assert.True(workstation.DocumentExists("mail"));
            // and the key it needed, created once and said out loud
            Assert.Equal([(ConfigurationServiceCollectionExtensions.KeyringService, Workstation.ApplicationName)], workstation.Keyring.Writes);
            Assert.Contains(
                workstation.Log.Messages,
                message =>
                    message.Contains("created configuration manifest section", StringComparison.OrdinalIgnoreCase)
            );
        }
    }

    /// <summary>UC-002. The manifest is canonical. A section naming a directory other than the default
    /// is simply obeyed — there is no API that competes with it, and nothing in the application has to
    /// know. Each user's manifest answers for that user's machine and no other.
    /// </summary>
    [Fact]
    public async Task AddNyssaConfiguration_ManifestDirectory_PreservesWorkstationIsolationAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var chosen = Path.Combine(workstation.Home, "SomeApp", "config");
            workstation.WriteManifest(
                "{ \"version\": 1, \"applications\": { \"SomeApp-cli\": { \"configurationDirectory\": "
                    + System.Text.Json.JsonSerializer.Serialize(chosen)
                    + " } } }"
            );

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync("plugin", "name", "some-plugin", cancellationToken: cancellationToken);
            // Another user's workstation has its own manifest and knows nothing about this one
            using (var other = new Workstation())
            {
                var services2 = other.CreateServices();
                services2.AddNyssaConfiguration(other.Locate);
                other.StartApplication(services2);
                await Assert.ThrowsAsync<ResultException>(async () =>
                {
                    await other.Host.GetRequiredService<IConfiguration>().GetAsync("plugin", "name", cancellationToken);
                });

                // Assert
                Assert.True(File.Exists(Path.Combine(chosen, "plugin.json")));
                Assert.False(workstation.DocumentExists("plugin")); // the default was not used, and not created
            }
        }
    }

    /// <summary>UC-010. A service ships its manifest beside it and says so in code. The developer's
    /// location wins: the user's manifest is never consulted, and a relative directory in the shipped
    /// manifest resolves beside that manifest, not beside the user's.
    /// </summary>
    [Theory]
    [InlineData("deploy")]
    [InlineData(".config")]
    public async Task AddNyssaConfiguration_ShippedManifest_UsesRelativeDirectoryAsync(string manifestFolder)
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            workstation.DeleteManifest(); // the user's manifest does not exist; the service does not need it
            var cancellationToken = TestContext.Current.CancellationToken;
            // The previous default folder also works when the consuming application selects it explicitly.
            var shipped = Path.Combine(workstation.Home, manifestFolder, "config-manifest.json");
            Workstation.WriteManifest(
                shipped,
                """
                {
                  "version": 1,
                  "applications": {
                    "SomeApp-cli": { "configurationDirectory": "data" }
                  }
                }
                """
            );

            // Act
            // Program.cs: services.AddNyssaConfiguration(options => options.Bootstrap.ConfigManifest = "/opt/someapp/config-manifest.json")
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                workstation.Locate(options);
                options.Bootstrap.ConfigManifest = shipped;
            });
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync(
                "mail",
                "password",
                "fictional",
                provider: "aesLocal",
                cancellationToken: cancellationToken
            );
            var read = await configuration.GetAsync("mail", "password", cancellationToken);

            // Assert
            Assert.Equal("fictional", read);
            Assert.True(File.Exists(Path.Combine(workstation.Home, manifestFolder, "data", "mail.json")));
            Assert.False(File.Exists(workstation.ManifestPath));
        }
    }

    /// <summary>UC-010. Custom paths create missing manifests and application sections while preserving other applications.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddNyssaConfiguration_MissingCustomManifestOrSection_ProvisionsApplicationAsync(
        bool manifestExists
    )
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var shipped = Path.Combine(workstation.Home, "deploy", "custom.json");
            if (manifestExists)
                Workstation.WriteManifest(
                    shipped,
                    """
                    { "version": 1, "applications": { "OtherApp": { "configurationDirectory": "other-data" } } }
                    """
                );
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                workstation.Locate(options);
                options.Bootstrap.ConfigManifest = shipped;
            });
            workstation.StartApplication(services);
            var configuration = workstation.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync("mail", "host", "example.test", cancellationToken: cancellationToken);
            var read = await configuration.GetAsync("mail", "host", cancellationToken);

            // Assert
            Assert.Equal("example.test", read);
            using (var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(shipped)))
            {
                var applications = manifest.RootElement.GetProperty("applications");
                Assert.Equal(
                    workstation.ConfigurationDirectory,
                    applications
                        .GetProperty(Workstation.ApplicationName)
                        .GetProperty("configurationDirectory")
                        .GetString()
                );
                if (manifestExists)
                    Assert.Equal(
                        "other-data",
                        applications.GetProperty("OtherApp").GetProperty("configurationDirectory").GetString()
                    );
            }
        }
    }

    /// <summary>UC-011. The manifest section names a vault by provider name and kind; the developer
    /// registered the vault package; the app names neither. Values written to that provider land in the
    /// vault and the local document holds only a pointer.
    /// </summary>
    [Fact]
    public async Task SetAsync_ManifestVault_StoresRemotePointerAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            workstation.VaultPackage = new FakeVaultFactory(); // Program.cs: services.AddContosoVault()
            workstation.WriteManifest(
                """
                {
                  "version": 1,
                  "applications": {
                    "SomeApp-cli": {
                      "configurationDirectory": "SomeApp-cli",
                      "keyVault": { "name": "contoso", "kind": "fake", "endpoint": "https://vault.contoso.example" }
                    }
                  }
                }
                """
            );

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync(
                "mail",
                "password",
                "fictional-vault-password",
                provider: "contoso",
                cancellationToken: cancellationToken
            );
            var read = await configuration.GetAsync("mail", "password", cancellationToken);

            // Assert
            Assert.Equal("fictional-vault-password", read);
            var vault = Assert.Single(workstation.VaultPackage.Created);
            Assert.Equal("contoso", vault.Settings.Name);
            Assert.Equal(new Uri("https://vault.contoso.example"), vault.Settings.Endpoint);
            Assert.Contains("fictional-vault-password", vault.Values.Values);
            Assert.DoesNotContain(
                "fictional-vault-password",
                workstation.ReadDocument("mail"),
                StringComparison.Ordinal
            );
            Assert.Contains("\"contoso\"", workstation.ReadDocument("mail"), StringComparison.Ordinal);
        }
    }

    /// <summary>UC-011, failure case. A kind nobody registered is a missing package reference, said plainly.</summary>
    [Fact]
    public void AddNyssaConfiguration_UnregisteredVaultKind_ThrowsAtResolution()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            workstation.VaultPackage = new FakeVaultFactory();
            workstation.WriteManifest(
                """
                {
                  "version": 1,
                  "applications": {
                    "SomeApp-cli": {
                      "configurationDirectory": "SomeApp-cli",
                      "keyVault": { "name": "hsm", "kind": "fabrikam" }
                    }
                  }
                }
                """
            );

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            var failure = Assert.Throws<InvalidOperationException>(() =>
                workstation.Host.GetRequiredService<IConfiguration>()
            );

            // Assert

            Assert.Contains("fabrikam", failure.Message, StringComparison.Ordinal);
            Assert.Contains("fake", failure.Message, StringComparison.Ordinal); // the kinds that are registered
        }
    }

    /// <summary>UC-017. An application that ships with a vault describes it in its bootstrap options
    /// instead of asking an operator to write it into the manifest. The section still names the directory;
    /// the vault comes from code.
    /// </summary>
    [Fact]
    public async Task AddNyssaConfiguration_CodeVault_UsesDeveloperDefaultsAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            workstation.VaultPackage = new FakeVaultFactory();

            // Act
            // Program.cs: services.AddNyssaConfiguration(options => options.Bootstrap.KeyVault = new VaultOptions { ... })
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                workstation.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions
                {
                    Name = "contoso",
                    Kind = "fake",
                    Endpoint = new Uri("https://vault.contoso.example"),
                    Settings = { ["tenant"] = "prod" },
                };
            });
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync(
                "mail",
                "password",
                "fictional-vault-password",
                provider: "contoso",
                cancellationToken: cancellationToken
            );
            var read = await configuration.GetAsync("mail", "password", cancellationToken);

            // Assert
            Assert.Equal("fictional-vault-password", read);
            var vault = Assert.Single(workstation.VaultPackage.Created);
            Assert.Equal("contoso", vault.Settings.Name);
            Assert.Equal(new Uri("https://vault.contoso.example"), vault.Settings.Endpoint);
            Assert.Equal("prod", vault.Settings.Settings["tenant"]);
            Assert.Contains("fictional-vault-password", vault.Values.Values);
            Assert.DoesNotContain(
                "fictional-vault-password",
                workstation.ReadDocument("mail"),
                StringComparison.Ordinal
            );
            // The library still created its own key
            Assert.True(workstation.Keyring.Holds(ConfigurationServiceCollectionExtensions.KeyringService, Workstation.ApplicationName));
        }
    }

    /// <summary>UC-017. The application's key vault is its default. An operator who names one in the
    /// manifest section owns everything about it: the manifest entry replaces the code entry whole.
    /// </summary>
    [Fact]
    public async Task AddNyssaConfiguration_ManifestVault_ReplacesCodeDefaultsAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            workstation.VaultPackage = new FakeVaultFactory();
            workstation.WriteManifest(
                """
                {
                  "version": 1,
                  "applications": {
                    "SomeApp-cli": {
                      "configurationDirectory": "SomeApp-cli",
                      "keyVault": { "name": "contoso", "kind": "fake", "endpoint": "https://vault.contoso.example/admin" }
                    }
                  }
                }
                """
            );

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                workstation.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions
                {
                    Name = "contoso",
                    Kind = "fake",
                    Endpoint = new Uri("https://vault.contoso.example/developer"),
                    Settings = { ["tenant"] = "dev" },
                };
            });
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync(
                "mail",
                "password",
                "fictional-vault-password",
                provider: "contoso",
                cancellationToken: cancellationToken
            );

            // Assert
            var vault = Assert.Single(workstation.VaultPackage.Created);
            Assert.Equal(new Uri("https://vault.contoso.example/admin"), vault.Settings.Endpoint);
            Assert.Empty(vault.Settings.Settings); // the code entry's settings went with it
        }
    }

    /// <summary>UC-017, failure case. A vault that cannot be right is refused when the configuration is
    /// resolved — at startup, before the application reads anything — naming the vault and what is wrong
    /// with it.
    /// </summary>
    [Fact]
    public void AddNyssaConfiguration_MalformedVault_ThrowsAtResolution()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            workstation.VaultPackage = new FakeVaultFactory();

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                workstation.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions
                {
                    Name = "contoso",
                    Kind = "fake",
                    Credential = "not-an-address",
                };
            });
            workstation.StartApplication(services);
            var failure = Assert.Throws<InvalidOperationException>(() =>
                workstation.Host.GetRequiredService<IConfiguration>()
            );

            // Assert

            Assert.Contains("contoso", failure.Message, StringComparison.Ordinal);
            Assert.Contains("not-an-address", failure.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>UC-018. A headless service cannot sign in, so its vault authenticates with secrets the
    /// operator wrote into the manifest section. The vault package receives them as data; the library never
    /// opens a browser, never logs them, and never repeats them.
    /// </summary>
    [Fact]
    public async Task SetAsync_ManifestSecrets_AuthenticatesWithoutBrowserAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            workstation.VaultPackage = new FakeVaultFactory();
            workstation.WriteManifest(
                """
                {
                  "version": 1,
                  "applications": {
                    "SomeApp-cli": {
                      "configurationDirectory": "SomeApp-cli",
                      "keyVault": {
                        "name": "contoso",
                        "kind": "fake",
                        "endpoint": "https://vault.contoso.example",
                        "settings": { "tenant": "prod" },
                        "secrets": { "clientSecret": "fictional-client-secret" }
                      }
                    }
                  }
                }
                """
            );

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync(
                "mail",
                "password",
                "fictional-vault-password",
                provider: "contoso",
                cancellationToken: cancellationToken
            );
            var read = await configuration.GetAsync("mail", "password", cancellationToken);

            // Assert
            Assert.Equal("fictional-vault-password", read);
            var vault = Assert.Single(workstation.VaultPackage.Created);
            Assert.Equal("fictional-client-secret", vault.Settings.Secrets["clientSecret"]);
            Assert.Equal("prod", vault.Settings.Settings["tenant"]);
            Assert.Equal(0, workstation.Browser.Launches);
            Assert.DoesNotContain(
                workstation.Log.Messages,
                message => message.Contains("fictional-client-secret", StringComparison.Ordinal)
            );
        }
    }

    /// <summary>UC-018, failure case. A manifest that carries secrets and that other accounts can read is
    /// refused at startup — naming the bit that exposes it, who that lets in, and the command that fixes it,
    /// and never the secret itself.
    /// </summary>
    [Fact(Skip = "POSIX mode bits are unavailable on Windows.", SkipWhen = nameof(IsWindows))]
    public void AddNyssaConfiguration_GroupReadableSecrets_ThrowsWithoutExposingSecret()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            workstation.VaultPackage = new FakeVaultFactory();
            workstation.WriteManifest(
                """
                {
                  "version": 1,
                  "applications": {
                    "SomeApp-cli": {
                      "configurationDirectory": "SomeApp-cli",
                      "keyVault": { "name": "contoso", "kind": "fake", "secrets": { "clientSecret": "fictional-client-secret" } }
                    }
                  }
                }
                """
            );
            // An operator left the file group-readable
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(
                    workstation.ManifestPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead
                );

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            var failure = Assert.Throws<InvalidOperationException>(() =>
                workstation.Host.GetRequiredService<IConfiguration>()
            );

            // Assert

            Assert.Contains("group", failure.Message, StringComparison.Ordinal);
            Assert.Contains("chmod 600", failure.Message, StringComparison.Ordinal);
            Assert.Contains(workstation.ManifestPath, failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("fictional-client-secret", failure.Message, StringComparison.Ordinal);
            Assert.Empty(workstation.VaultPackage.Created);
        }
    }

    /// <summary>UC-018, Windows failure case. A manifest that carries secrets and grants read access to
    /// Everyone is refused at startup — naming the ACL remediation command and never the secret itself.
    /// </summary>
    [Fact(Skip = "Windows ACLs are unavailable on non-Windows platforms.", SkipUnless = nameof(IsWindows))]
    public void AddNyssaConfiguration_EveryoneReadableSecrets_ThrowsWithoutExposingSecret()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            workstation.VaultPackage = new FakeVaultFactory();
            workstation.WriteManifest(
                """
                {
                  "version": 1,
                  "applications": {
                    "SomeApp-cli": {
                      "configurationDirectory": "SomeApp-cli",
                      "keyVault": { "name": "contoso", "kind": "fake", "secrets": { "clientSecret": "fictional-client-secret" } }
                    }
                  }
                }
                """
            );
            if (OperatingSystem.IsWindows())
            {
                var info = new FileInfo(workstation.ManifestPath);
                var security = info.GetAccessControl();
                security.AddAccessRule(
                    new System.Security.AccessControl.FileSystemAccessRule(
                        new System.Security.Principal.SecurityIdentifier(
                            System.Security.Principal.WellKnownSidType.WorldSid,
                            null
                        ),
                        System.Security.AccessControl.FileSystemRights.Read,
                        System.Security.AccessControl.AccessControlType.Allow
                    )
                );
                info.SetAccessControl(security);
            }

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            var failure = Assert.Throws<InvalidOperationException>(() =>
                workstation.Host.GetRequiredService<IConfiguration>()
            );

            // Assert
            Assert.Contains("readable", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("icacls", failure.Message, StringComparison.Ordinal);
            Assert.Contains(workstation.ManifestPath, failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("fictional-client-secret", failure.Message, StringComparison.Ordinal);
            Assert.Empty(workstation.VaultPackage.Created);
        }
    }
}
