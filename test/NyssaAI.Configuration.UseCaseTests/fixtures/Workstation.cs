using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>
/// One isolated workstation for one tutorial: its own home directory, manifest, credential store,
/// clock, log, browser, and authorization server. Nothing here touches the real OS, and every
/// container it builds is what SomeApp-cli would build.
/// </summary>
/// <remarks>
/// A fresh workstation already has the manifest an <c>init</c> run, or an operator, leaves
/// behind: one section for SomeApp-cli naming the directory beside the manifest. A tutorial that
/// needs a different manifest writes its own; one that needs none deletes it.
/// </remarks>
public sealed class Workstation : IDisposable
{
    public const string ApplicationName = "SomeApp-cli";

    /// <summary>The manifest a fresh workstation carries: one section, the directory beside it.</summary>
    public const string DefaultManifest = """
        {
          "version": 1,
          "applications": {
            "SomeApp-cli": { "configurationDirectory": "SomeApp-cli" }
          }
        }
        """;

    // Inside the repository, never the user's profile and never the machine's temp folder. See RepositoryTemp.
    private readonly DirectoryInfo _root = RepositoryTemp.CreateHome("workstation");

    public Workstation()
    {
        Home = Directory.CreateDirectory(Path.Combine(_root.FullName, "home")).FullName;
        WriteManifest(DefaultManifest);
    }

    /// <summary>The user's home; the manifest lives under its .nyssa-ai.</summary>
    public string Home { get; }

    /// <summary>The user's manifest: <c>~/.nyssa-ai/config-manifest.json</c>.</summary>
    public string ManifestPath => Path.Combine(Home, ".nyssa-ai", "config-manifest.json");

    /// <summary>Where this fixture's manifest section points: <c>~/.nyssa-ai/SomeApp-cli</c>.</summary>
    public string ConfigurationDirectory => Path.Combine(Home, ".nyssa-ai", ApplicationName);

    public FakeKeyring Keyring { get; } = new();

    public RecordingLoggerProvider Log { get; } = new();

    public FakeClock Clock { get; } = new();

    public FakeBrowser Browser { get; } = new();

    public FakeAuthorizationServer AuthorizationServer { get; } = new();

    /// <summary>A vault package the developer referenced and registered, when the tutorial has one.</summary>
    public FakeVaultFactory? VaultPackage { get; set; }

    /// <summary>What an operator does: place a manifest. Private to the user, as an operator would leave it.</summary>
    public void WriteManifest(string json) => WriteManifest(ManifestPath, json);

    /// <summary>Place a manifest somewhere else — what a developer ships beside a service.</summary>
    public static void WriteManifest(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>A machine nobody has prepared: no manifest at all.</summary>
    public void DeleteManifest() => File.Delete(ManifestPath);

    /// <summary>What an administrator may do: place the encryption key in the credential store ahead of time.</summary>
    public void ProvisionKey() =>
        Keyring.Set(
            ApplicationName,
            "configuration-key",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        );

    /// <summary>Read a namespace document as the bytes on disk, to prove what is and is not in it.</summary>
    public string ReadDocument(string @namespace) =>
        File.ReadAllText(Path.Combine(ConfigurationDirectory, $"{@namespace}.json"));

    public bool DocumentExists(string @namespace) =>
        File.Exists(Path.Combine(ConfigurationDirectory, $"{@namespace}.json"));

    /// <summary>
    /// This machine, as a service collection: everything <c>Program.cs</c> registers <em>except</em>
    /// this library, so a tutorial can write the <c>AddNyssaConfiguration</c> line itself and show
    /// which shape of it the application chose.
    /// </summary>
    /// <param name="application">The application's own registrations, if the tutorial has any.</param>
    public IServiceCollection CreateServices(Action<IServiceCollection>? application = null)
    {
        var services = new ServiceCollection();
        application?.Invoke(services);
        Register(services);
        return services;
    }

    /// <summary>
    /// The same registrations, onto a collection someone else owns — a host builder's
    /// <c>builder.Services</c>, for a tutorial that builds the generic host itself.
    /// </summary>
    /// <param name="services">The collection to register this workstation's stand-ins with.</param>
    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging(builder =>
            builder.AddProvider(Log).SetMinimumLevel(LogLevel.Information)
        );
        services.AddSingleton<IKeyring>(Keyring);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IBrowserLauncher>(Browser);
        services.ConfigureHttpClientDefaults(client =>
            client.ConfigurePrimaryHttpMessageHandler(() => AuthorizationServer)
        );
        if (VaultPackage is not null)
            services.AddFakeVault(VaultPackage); // Program.cs: services.AddContosoVault()
    }

    /// <summary>
    /// Points startup options at this workstation. An application never writes this: its manifest
    /// is beneath the real profile directory and its configuration beneath the real working
    /// directory, and both of these are temporary folders that have to be named. The two anchors
    /// move independently, so both are set.
    /// </summary>
    /// <param name="options">The options the registration call will read.</param>
    public void Locate(NyssaConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Bootstrap.ConfigManifest = ManifestPath;
        options.ConfigDirectory = ConfigurationDirectory;
    }

    private readonly List<DISimulator> _applications = [];

    /// <summary>The current application's scoped provider; the workstation owns its lifetime.</summary>
    public IServiceProvider Host => _applications[^1].Services;

    /// <summary>Opens another caller scope within the current application.</summary>
    public IConfiguration RefreshedConfigurationService() => _applications[^1].NextConfiguration();

    /// <summary>Stops the current application while retaining this workstation's files and credentials.</summary>
    public void StopApplication()
    {
        var current = _applications[^1];
        _applications.RemoveAt(_applications.Count - 1);
        current.Dispose();
    }

    /// <summary>Owns the host and scope for the registrations shown in the test.</summary>
    public DISimulator StartApplication(IServiceCollection services)
    {
        var simulator = new DISimulator(services);
        _applications.Add(simulator);
        return simulator;
    }

    public void Dispose()
    {
        foreach (var application in _applications)
            application.Dispose();
        RepositoryTemp.DeleteHome(_root);
    }
}
