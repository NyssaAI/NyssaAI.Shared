# Client

`NyssaAI.Configuration.Client.ConfigurationServiceCollectionExtensions` registers the library through
`AddNyssaConfiguration()`, an options instance, or a callback editing `NyssaConfigurationOptions`.
The application consumes the root `IConfiguration` and `IConfigurationExtensions` interfaces.

`NyssaConfigurationOptions` contains `ApplicationName`, `ConfigDirectory`, and `Bootstrap`.
Application identity comes from the entry assembly's `AssemblyTitle` or assembly name unless
overridden. The data-directory default is `<pwd>/.nyssa-ai/config`; the manifest default is
`~/.nyssa-ai/config-manifest.json`. Existing manifest sections determine the directory used.

`BootstrapOptions` supplies the manifest path, lock timeout, and optional `VaultOptions`.
The manifest can replace the vault configuration and lock timeout. `VaultOptions` describes a
registered factory's provider name, kind, endpoint, credential reference, settings, and secrets.
`OAuthOptions` describes the authorization flow explicitly requested through the extensions contract.

Registration is scoped and the first registration wins. The instance overload retains its options
object; finish configuring it before resolving a scope. On first resolution, registration composes
the manifest reader/writer, OS keyring, local encryption, OAuth, optional vault, and configuration
store. Initialization failures surface as `InvalidOperationException` at resolution.

`ConfigurationClient` delegates value operations to the store and implements interactive extension
operations explicitly. Applications can supply `IKeyring`, `TimeProvider`, `IBrowserLauncher`,
`IHttpClientFactory`, and vault factories through dependency injection.

See the [package README](../../README.md) for setup and complete settings examples.
