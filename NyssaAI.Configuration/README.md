# NyssaAI.Configuration

`NyssaAI.Configuration` stores settings and secrets for .NET applications and command-line tools. Application code uses [`IConfiguration`](src/IConfiguration.cs) to read and write values that persist across process runs, whether they are stored as plain JSON, encrypted locally, or held in a remote vault. The library is designed to run on Windows, Linux, and macOS, with support for Native AOT compilation.

## Easy setup, consistent access

Supply a configuration settings type, register configuration, and save or retrieve that object in one call. The library can handle the files, JSON serialization, and encryption by default.

- **Add default configuration.** `AddNyssaConfiguration()` uses the application's assembly identity and default storage paths. On first use, it creates a missing manifest section and prepares the configuration directory.
- **Keep each module's settings together.** Give each module its own namespace. Save its complete settings object, including nested objects, with `SetAsAsync("orders", orderSettings)`, and retrieve it with `GetAsAsync<OrderSettings>("orders")`. Each top-level property becomes an entry. New properties use plaintext; existing properties keep their storage provider.
- **Add encryption with a storage choice.** Select `"aesLocal"` when saving a new entry. The library creates and manages the encryption key in the OS credential store; reads return the original DTO.
- **Use a remote vault through the same API.** After registering a vault provider and configuring its connection and credentials, use its provider name when saving. The value stays in the vault, and the library keeps a local pointer. The module's read call stays the same. Use `MoveAsync` to change the provider for an existing entry.

Local storage and encryption work out of the box on a supported machine with an available OS credential store. You do not need to choose file paths, generate keys, or prepare a manifest for the default setup.

### Example: two modules using the defaults

Suppose an application has an Orders module and a Notifications module. Each module stores its settings in a separate namespace. Here, `configuration` is an injected `IConfiguration`, and the settings objects are already populated.

```csharp
// Save each module's settings as plain JSON.
(await configuration.SetAsAsync("orders", orderSettings)).ThrowIfError();
(await configuration.SetAsAsync("notifications", notificationSettings)).ThrowIfError();

// Read the complete settings objects.
var orders = (await configuration.GetAsAsync<OrderSettings>("orders")).ThrowIfError();
var notifications = (await configuration.GetAsAsync<NotificationSettings>("notifications")).ThrowIfError();
```

Each save updates the DTO's properties in its namespace, keeping nested objects together. Registration prepares the service; the `SetAsAsync` calls supply the values. The same read call assembles the DTO even when some properties use local encryption or a cloud vault.

## IConfiguration - The main interface

Once configuration is registered, inject `IConfiguration` into the classes that need it. The storage choice is recorded with each key, so a database service uses the same call to read a password encrypted on disk or stored in a vault.

```csharp
var host = (await configuration.GetAsync("database", "host")).ThrowIfError();
var password = (await configuration.GetAsync("database", "password")).ThrowIfError();
```

Application code uses `IConfiguration` to read and write values, regardless of where they are stored.

The API below is organized around `IConfiguration`. Each method accepts an optional cancellation token and returns a `ValueTask`; the tree shows the result after awaiting it.

```text
IConfiguration
|-- Plain                Explicitly selects unencrypted storage ("")
|-- GetAsync             Result<string>: NotFound when the key is missing
|-- SetAsync             Result: new keys require a provider; updates can inherit it
|-- GetAsAsync<TValue>   Result<TValue>: read a named key or the namespace settings
|-- SetAsAsync<TValue>   Result: write a named key or namespace properties
|-- RemoveAsync          Result: remove the local entry; leave any remote secret
|-- ListAsync            Result<IReadOnlyList<string>>: keys in ordinal order
`-- MoveAsync            Result<ConfigurationMove>: change provider and report source cleanup
```

The methods have a few behaviors to account for when using them:

- `GetAsync` returns `NotFound` when the key is missing.
- `ListAsync` returns keys in ordinal order, or an empty list when the namespace does not exist.
- `RemoveAsync` succeeds when the key is already absent. For a key stored in a vault, it removes the local pointer and leaves the remote secret in place.
- `GetAsAsync<TValue>` and `SetAsAsync<TValue>` each have reflection-based and source-generated JSON overloads.

Operations report failures through [`Result` and `Result<T>`](../NyssaAI.Result/src/Result.cs) from [`NyssaAI.Results`](../NyssaAI.Result/README.md). Check the result before using a value or starting an operation that depends on it. The examples use `ThrowIfError()` to stop execution when an operation fails.

Startup can also fail. Resolving the service can throw `InvalidOperationException` if startup is not successful.

## Data storage

### The manifest

The manifest is a JSON file that tells the library where to find each application's configuration data and how to open it. The manifest is kept at `~/.nyssa-ai/config-manifest.json` by default, where `~` is the current user's home directory. One manifest can contain sections for several applications.

For example, a manifest can map `OrdersApp` to `D:/apps/orders/config`. The `orders` namespace then uses `D:/apps/orders/config/orders.json`.
```json
{
  "version": 1,
  "applications": {
    "OrdersApp": {
      "configurationDirectory": "D:/apps/orders/config"
    }
  }
}
```
* Once that directory is recorded in the manifest, it is canonical. The app code can suggest a location, but the manifest is what is used for locating the configuration data.

In addition, the manifest can specify a secure key vault, its connection and authentication settings. If those details are present, the library will load the key vault provider and will enable it to keep encrypted secrets in a cloud vault; the local files will hold a pointer to that cloud vault secret.
```json
{
  "version": 1,
  "applications": {
    "OrdersApp": {
      "configurationDirectory": "D:/apps/orders/config",
      "keyVault": {
        "name": "mailVault",
        "kind": "bitwarden-secrets",
        "settings": {
          "organizationId": "11111111-1111-4111-8111-111111111111"
        },
        "secrets": {
          "accessToken": "<bitwarden-machine-account-access-token>"
        }
      }
    }
  }
}
```

This example uses the Bitwarden Secrets provider. Register its factory and replace the example organization ID and access token with your own values.

You can specify a custom manifest location in application code, and use that manifest for as many applications as you see fit. If no path is supplied by application code, the manifest will be created in the default location.

### The configuration folder

After reading the manifest, the library prepares the application's configuration folder. For a new application using the defaults, this is `.nyssa-ai/config` beneath the application's base directory (`AppContext.BaseDirectory`). The manifest records that location for future use.

Each namespace has a JSON file in that folder. Each top-level DTO property has its own entry and storage provider. Nested objects remain together within their property's entry.

For example, `mailserverSettings.json` can build one DTO from plain settings, a locally encrypted password, and an API key stored in a cloud vault:

```json
{
  "Host": {
    "value": "smtp.example.com",
    "encrypted": false,
    "provider": "",
    "reauth_required": false
  },
  "Port": {
    "value": 587,
    "encrypted": false,
    "provider": "",
    "reauth_required": false
  },
  "UseTls": {
    "value": true,
    "encrypted": false,
    "provider": "",
    "reauth_required": false
  },
  "Password": {
    "value": "<encrypted password>",
    "encrypted": true,
    "provider": "aesLocal",
    "reauth_required": false
  },
  "ApiKey": {
    "value": "mailserverSettings/ApiKey",
    "encrypted": false,
    "provider": "mailVault",
    "reauth_required": false
  }
}
```

The encrypted password is a placeholder. The pointer `mailserverSettings/ApiKey` uses the Bitwarden Secrets implementation's `namespace/key` format. The adapter finds the secret with that name in the configured organization and retrieves its value. `mailVault` is the configured vault name; it must match the manifest's `keyVault.name`, with the Bitwarden factory registered for kind `bitwarden-secrets`. `encrypted` describes the local value, so it is `false` for a vault pointer.

`GetAsAsync<MailserverSettings>("mailserverSettings")` resolves all five entries and builds one DTO, including `Password` and `ApiKey`. Entry names match the DTO's serialized property names. If any entry cannot be resolved, the read returns a failure.

### The key store

Once the configuration folder is ready, the library reads the application's encryption key from the OS credential store. If the key is missing, the library generates and stores one. This happens even when the application only uses plaintext settings.

The key is used to encrypt and decrypt local values. It stays in the OS credential store, separate from the manifest and namespace files.

### Startup order

Registration prepares the service. Initialization happens when `IConfiguration` is first resolved within a dependency-injection scope.

1. **Register configuration.** `AddNyssaConfiguration()` records the application's identity and startup options.
2. **Read the manifest.** Find the application's section, creating it if missing. Use its configuration directory and any vault or lock-timeout overrides.
3. **Prepare local storage.** Create the configuration directory if needed and check that it can be used safely.
4. **Prepare encryption.** Read the application's encryption key from the OS credential store, or generate and store one if it is missing. This step also applies to applications that only use plaintext settings.
5. **Prepare the providers.** Create the local encryption and OAuth providers, plus the remote vault adapter when a vault is configured.
6. **Make configuration available.** Connect the local files and providers to the configuration service. Settings are loaded when requested, rather than loading every namespace during startup.

### Read order

A read starts with the local namespace file, including when the value is stored in a remote vault.

1. **Select the entry.** Use the requested namespace and key. When the key is omitted, read the namespace entries to assemble the DTO.
2. **Read the namespace file.** Find the entry and its recorded provider. A missing entry returns `NotFound`.
3. **Resolve the value.** Return a plain value directly, decrypt a local value, or ask the named vault provider to retrieve the remote secret. For an OAuth entry, obtain the access token and refresh it when needed.
4. **Return the result.** `GetAsync` returns a string. `GetAsAsync<T>` deserializes the value into the requested settings type. Failures are returned through `Result`.

Each read uses the provider recorded with that entry.

## Library Architecture

`ConfigurationClient` is the client implementation that exposes `IConfiguration` to your application and manages calls to the underlying `ConfigurationStore`. The store reads and writes namespace files, assembles DTOs, and calls the appropriate storage providers.

```text
+---------------------+
|     Application     |
+----------+----------+
           |
     IConfiguration
           |
           v
+---------------------+     +---------------------+     IConfigurationProvider
| ConfigurationClient | <-> | ConfigurationStore  | ---+-- Local encryption (aesLocal)
+---------------------+     +---------------------+    +-- OAuth (oauth)
                                                       +-- Cloud vault (e.g. Bitwarden)
```

### IConfiguration

[`IConfiguration`](src/IConfiguration.cs) defines the operations application code uses: read, write, list, remove, and move settings. Its typed methods save and restore complete settings objects. This is the interface your application uses for ordinary settings access.

OAuth sign-in uses a separate interface, [`IConfigurationExtensions`](src/IConfigurationExtensions.cs), for authorization and reauthorization. Once signed in, application code reads the access token through `IConfiguration`; the library handles token refresh.

### ConfigurationStore

[`ConfigurationStore`](src/Values/ConfigurationStore.cs) coordinates all configuration reads and writes. It receives calls through `ConfigurationClient`, reads the namespace file, and uses each entry's provider to retrieve or store its value. For instance:

- If a plain setting is requested, it returns the value from the local file.
- If an encrypted password is requested, it uses the local encryption provider to decrypt it.
- If a cloud secret is requested, it passes the stored pointer to the configured vault provider and retrieves the secret.
- If a complete settings DTO is requested, it resolves the namespace's entries and builds the DTO from those values.

When saving a DTO, the store keeps each existing property's provider and saves new properties as plaintext. To change a property's provider, use `MoveAsync`. The store saves the value at its new location before attempting to remove the old copy.

### Providers

Providers handle the parts of storage that depend on encryption or an external service. Internally, they implement [`IConfigurationProvider`](src/Contracts/IConfigurationProvider.cs):

- **`Name`** identifies the provider recorded with an entry, such as `"aesLocal"` or a configured vault name.
- **`WriteAsync`** stores or encrypts the value and returns the entry to save locally.
- **`ReadAsync`** turns that stored entry back into a usable value.
- **`EraseAsync`** withdraws a provider's old copy when a value moves elsewhere. Replacing the local entry is enough for locally stored encrypted data; a vault adapter must also withdraw the remote copy.

The built-in providers handle local encryption and OAuth credentials, including token refresh. Plain values are handled directly by the configuration store.

Vault packages implement the public `ICloudKeyVaultFactory` and `ICloudKeyVault` contracts. The library wraps the vault in an internal provider and connects it to the store. Application code continues to call `IConfiguration`; it does not call the vault adapter directly. See [Connect a remote vault](#connect-a-remote-vault) for the provider setup.

## Working with settings

### Store a namespace's default settings

When a module has one settings object, you can save it with just the namespace and the object:

```csharp
(await configuration.SetAsAsync("ordersNamespace", orderDto)).ThrowIfError();
var settings = (await configuration.GetAsAsync<OrderSettings>("ordersNamespace")).ThrowIfError();
```

Saving writes each top-level property to its own entry, preserving existing providers and using plaintext for new properties. Nested objects stay together, and omitted keys remain unchanged. Reading resolves the entries and assembles the DTO; an empty namespace returns `NotFound`.

Both methods also accept a source-generated `JsonTypeInfo<T>` after the namespace. For writes, pass the object after the type information: `SetAsAsync("ordersNamespace", typeInfo, orderDto)`. Use these overloads for Native AOT or trimmed applications.

Use `SetAsync` to create a string property with a chosen provider, or `MoveAsync` to change an existing property's provider. Whole-DTO saves then preserve those choices. Remote writes cannot be atomic across providers: a later failure can leave earlier remote updates in place.

Older files containing a `namespaceSettings` entry remain readable. A whole-DTO save migrates a plaintext legacy object into individual property entries. A protected legacy object returns `Conflict` on migration; explicitly move it to plaintext first and choose providers for its properties. Scalar namespace values retain their legacy single-entry format.

### Choose where a value is stored

Each key records the provider that stores its value. Choose that provider when you create the key; later writes can use the recorded choice.

| Provider | Storage |
| --- | --- |
| `IConfiguration.Plain` (`""`) | Plain JSON in the local namespace file. |
| `"aesLocal"` | Local AES-256-GCM encryption, with the encryption key held in the OS credential store. |
| A configured vault name | The secret stays in the remote vault; the local file holds a pointer to it. |

```csharp
// Create a plain setting.
(await configuration.SetAsync("server", "port", "8080", IConfiguration.Plain)).ThrowIfError();

// Create an encrypted secret. secretKey comes from the caller.
(await configuration.SetAsync("server", "apiKey", secretKey, "aesLocal")).ThrowIfError();

// Update the secret using its recorded provider.
(await configuration.SetAsync("server", "apiKey", newSecretKey)).ThrowIfError();

// Move the port setting from plain storage to local encryption.
var moved = (await configuration.MoveAsync("server", "port", "aesLocal")).ThrowIfError();
```

For the overloads that take an explicit key, the library selects the provider as follows:

1. A new key must name its provider. Omitting it returns a failure. To use plain storage, pass `IConfiguration.Plain`.
2. For an existing key, omitting `provider` or passing `null` keeps the current provider.
3. Passing the same provider is allowed. Passing a different one to `SetAsync` returns `ErrorCategory.Conflict`.
4. Use `MoveAsync` to change providers. Moving to the current provider returns a failure.

A move makes the value available through the new provider, then tries to remove the old copy. Check [`ConfigurationMove.SourceWithdrawn`](src/Contracts/ConfigurationMove.cs) after the move. If it is `false`, you can use the value at its destination, but a copy remains at the source. The library also logs a warning when this happens.

### Store typed objects

Settings that belong together can be stored as one object, including nested objects and collections. Here, `mailSettings` is a populated `MailSettings` object containing a host, port, and credentials. Use local encryption because it contains a password:

```csharp
(await configuration.SetAsAsync("mail", "smtp", mailSettings, "aesLocal")).ThrowIfError();
var settings = (await configuration.GetAsAsync<MailSettings>("mail", "smtp")).ThrowIfError();
```

The object is stored under the `smtp` key in `mail.json`. Reading the key restores the complete object, including its credentials.

For Native AOT or trimmed applications, pass a source-generated `JsonTypeInfo<T>` for both reads and writes. Here, `typeInfo` comes from your JSON context for `MailSettings`. The reflection-based overloads carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` annotations.

```csharp
(await configuration.SetAsAsync("mail", "smtp", mailSettings, typeInfo, "aesLocal")).ThrowIfError();
var settings = (await configuration.GetAsAsync("mail", "smtp", typeInfo)).ThrowIfError();
```

## Register the library

Register the library with Microsoft dependency injection. The configuration service is scoped, so resolve `IConfiguration` within a scope and inject it into the classes that need it.

```csharp
services.AddNyssaConfiguration();
```

[`AddNyssaConfiguration`](src/Client/ConfigurationServiceCollectionExtensions.cs) supports three ways to register the service:

- Call it without arguments to use the defaults.
- Pass a [`NyssaConfigurationOptions`](src/Client/NyssaConfigurationOptions.cs) instance with your settings.
- Pass a delegate to change the options, as shown below.

The first registration wins if more than one module registers the library.

The first time the service is resolved, it reads the application's manifest section, prepares the data directory, and obtains or creates the local encryption key. It uses these defaults:

| Setting | Default |
| --- | --- |
| Application name | Entry assembly's `AssemblyTitle`, falling back to its assembly name. |
| Manifest | `~/.nyssa-ai/config-manifest.json` |
| Data directory for a new application section | `<AppContext.BaseDirectory>/.nyssa-ai/config` |
| Namespace file | `<configurationDirectory>/<namespace>.json` |
| File lock timeout | 10 seconds |

Set options when your application needs a different identity, location, or lock timeout:

```csharp
services.AddNyssaConfiguration(options =>
{
    options.ApplicationName = "CustomApp-cli";
    options.ConfigDirectory = Path.GetFullPath("var/data/config");
    options.Bootstrap.ConfigManifest = Path.GetFullPath("deploy/config-manifest.json");
    options.Bootstrap.LockTimeout = TimeSpan.FromSeconds(5);
});
```

This example uses a custom manifest location. The library creates `deploy/config-manifest.json` and the application's section if they are missing.

### How startup uses the manifest

The manifest records where each application's settings are stored. An operator can also use it to override the vault and lock timeout supplied by the developer. The following rules determine which settings are used:

1. **Identify the application.** When options are constructed, the application name comes from the entry assembly's title, or its name if the title is blank or absent. Set `ApplicationName` to use another value. Constructing options without an entry assembly throws `InvalidOperationException`.
2. **Read the application's section.** At any manifest location, the library creates a missing file or application section using `ConfigDirectory`. An invalid, unreadable, or unsafe manifest causes initialization to fail.
3. **Use the recorded data directory.** Once the application's section exists, its `configurationDirectory` determines where settings are stored. Relative paths resolve against the manifest's directory. Changing `ConfigDirectory` does not move existing files or replace the recorded path.
4. **Apply bootstrap overrides.** A vault defined in the manifest replaces all of `BootstrapOptions.KeyVault`. A manifest lock timeout overrides `BootstrapOptions.LockTimeout`.

For example, suppose `D:/apps/tool/config-manifest.json` records `./data` as the configuration directory. The `mail` namespace is stored at `D:/apps/tool/data/mail.json`, even if `options.ConfigDirectory` points to another location.

### Where the encryption key is stored

Startup initializes local encryption even if your application only intends to store plain values. It reads a 256-bit key from the OS credential store at `(ApplicationName, "configuration-key")`. If the key is missing, the library generates and stores one there.

| Platform | Credential store |
| --- | --- |
| Windows | Windows Credential Manager, through `CredRead` and `CredWrite`. |
| macOS | Apple Keychain Services, through `SecItem*`. |
| Linux | FreeDesktop Secret Service, through D-Bus and libsecret. |

An available credential store is required at startup. If it is unavailable, resolving `IConfiguration` throws `InvalidOperationException`; the library does not fall back to unprotected storage. You still choose the provider for each value.

## Add interactive sign-in

Use [`IConfigurationExtensions`](src/IConfigurationExtensions.cs) for setup commands that ask the user to sign in through a browser. Ordinary services use `IConfiguration`. If a read needs sign-in, it returns a failure so your application can decide when to involve the user.

Here, `authorization` is an injected `IConfigurationExtensions`, and `oauthOptions` contains the server endpoints, client ID, and requested scopes.

```csharp
(await authorization.AuthorizeAsync("mail", "token", oauthOptions)).ThrowIfError();
```

[`OAuthOptions`](src/Client/OAuthOptions.cs) describes the authorization server and public client. There are a few requirements for this setup:

- Both endpoints must use HTTPS.
- The sign-in flow uses PKCE and a local loopback listener.
- `RedirectPort` is optional unless the server requires a fixed port.

All three interactive methods return `ValueTask<Result>`:

| Method | When to use it |
| --- | --- |
| `AuthorizeAsync(@namespace, key, options, ct)` | Sign in to a service. Opens the browser, exchanges the authorization code, and encrypts the credentials under provider `"oauth"`. |
| `AuthorizeProviderAsync(provider, ct)` | Sign in to a configured vault. The provider supplies the server details; its configuration names the credential entry. |
| `ReauthorizeAsync(@namespace, key, ct)` | Sign in again after a grant is revoked, using the server details already stored with the entry. |

### What happens when a token expires

Reading an OAuth entry with `GetAsync` returns its access token. If the token is within five minutes of expiry, the library refreshes it before returning the value.

Readers use the namespace document lock to coordinate access. Each reader checks the stored token again after acquiring the lock, allowing waiting readers to use a token that another reader has just refreshed.

If the server rejects the refresh grant, the library marks the entry as requiring reauthorization. Reads then return `ErrorCategory.Forbidden` without opening a browser. Call `ReauthorizeAsync` from an interactive command to obtain a new grant.

## Connect a remote vault

A vault adapter stores the secret in the remote vault and records a [`ConfigurationPointer`](src/Contracts/ConfigurationPointer.cs) locally. Your application continues to read and write through `IConfiguration`. Integrations for specific services, such as Azure Key Vault, HashiCorp Vault, or Bitwarden, belong in separate provider packages.

A provider package implements two contracts:

- [`ICloudKeyVaultFactory`](src/Contracts/ICloudKeyVaultFactory.cs) creates the vault adapter. Its `Kind` must match the configured vault kind. `Create(VaultOptions settings, IAccessTokenSource? tokens)` returns `Result<ICloudKeyVault>`, and `Authorization(VaultOptions settings)` supplies OAuth options or `null` for another authentication method.
- [`ICloudKeyVault`](src/Contracts/ICloudKeyVault.cs) reads, writes, and removes remote secrets. A write returns the pointer to store locally. A read distinguishes a missing secret from a failure to contact the vault.

For vaults that use OAuth, the library supplies [`IAccessTokenSource`](src/Contracts/IAccessTokenSource.cs). The adapter uses it to request a current bearer token, and the library manages token refresh.

Register the vault factory before `AddNyssaConfiguration()`. Here, your provider package supplies `CustomVaultFactory`, and `vaultOptions` contains the vault's connection settings:

```csharp
services.AddSingleton<ICloudKeyVaultFactory, CustomVaultFactory>();
services.AddNyssaConfiguration(options => options.Bootstrap.KeyVault = vaultOptions);
```

[`VaultOptions`](src/Client/NyssaConfigurationOptions.cs) holds the provider name, kind, endpoint, credential entry, and any settings and secrets specific to the provider. If the vault uses OAuth, set `Credential` to a local `namespace/key` entry. The factory must supply authorization options for that entry.

The [contracts reference](src/Contracts/README.md) describes the provider contracts and related public types.

### Replace external dependencies in tests

Tests can supply their own credential store, browser launcher, clock, and HTTP clients. Register these implementations before `AddNyssaConfiguration()` so the tests can run independently of those external services.

| Dependency | What a test can replace |
| --- | --- |
| [`IKeyring`](src/Contracts/IKeyring.cs) | The OS credential store. |
| [`IBrowserLauncher`](src/Contracts/IBrowserLauncher.cs) | Browser launch during OAuth sign-in. |
| `TimeProvider` | The clock used to check token expiry. |
| `IHttpClientFactory` | HTTP clients used by the library's OAuth flow. |

## File permissions and concurrent access

Configuration files may contain settings, encrypted values, or bootstrap secrets. The library restricts access to files it creates and checks existing configuration locations before using them.

- **Unix permissions.** New configuration directories use `0700` permissions and files use `0600`. Existing paths are checked for unsafe permission bits. Unix ownership is not checked.
- **Windows permissions.** New directories restrict access to the current user, SYSTEM, and Administrators. Checks on existing paths include ownership and inherited permissions.
- **Bootstrap secrets.** Manifests containing these secrets must also pass the checks for private files.
- **Concurrent access.** Namespace reads and writes use cross-process locks, with retries up to the configured lock timeout.
- **File writes.** The library writes to a temporary file in the same directory, then replaces the destination atomically. Readers do not see a partially written document.

See the [file security reference](src/FileSecurity/README.md) for the platform checks.

## Why this library exists

Managing settings, local secrets, cloud credentials, and OAuth tokens often means writing separate code for each. This library gives application classes one interface for those operations, with an explicit storage choice for each key.

The library handles the work that would otherwise need to be repeated across applications:

- Persisting typed settings as JSON across process runs.
- Managing a local encryption key through the OS credential store.
- Connecting provider packages so application classes can use `IConfiguration` to access a vault.
- Refreshing OAuth tokens during reads and keeping interactive sign-in in setup commands.
- Coordinating file access across processes and reporting operation failures through `Result`.

An application can start with the defaults and select different paths, providers, or bootstrap settings as its needs change.

## Run the tutorials and tests

The [executable tutorials](docs/tutorials/README.md) show how to use the public API. They use isolated storage and fake external services so you can explore the behavior through tests.

| Tutorial | What it covers |
| --- | --- |
| [ManifestTutorials](../test/NyssaAI.Configuration.UseCaseTests/ManifestTutorials.cs) | First-use setup, paths, overrides, and manifest precedence. |
| [SetupTutorials](../test/NyssaAI.Configuration.UseCaseTests/SetupTutorials.cs) | Application identity, startup failures, encryption, and vault setup. |
| [ConfigurationTutorials](../test/NyssaAI.Configuration.UseCaseTests/ConfigurationTutorials.cs) | Plain and encrypted values, nested objects, persistence across scopes, provider moves, and source-generated JSON. |
| [NamespaceSettingsTutorials](../test/NyssaAI.Configuration.UseCaseTests/NamespaceSettingsTutorials.cs) | Default namespace settings, plaintext storage, updates, isolation, and source-generated JSON. |
| [OAuthTutorials](../test/NyssaAI.Configuration.UseCaseTests/OAuthTutorials.cs) | Sign-in, token refresh, revoked grants, and provider authentication. |

Run all included tests from the repository root:

```powershell
./run-tests.ps1
```

Live OS keyring tests are skipped by default. To run them against your machine's credential store, set `NYSSA_OS_KEYRING_TESTS=1`. These tests create and delete entries under `nyssa.configuration.tests`.

## Package and licensing

The package and assembly are both named `NyssaAI.Configuration`. The public namespaces are `NyssaAI.Configuration`, `NyssaAI.Configuration.Client`, and `NyssaAI.Configuration.Contracts`.

Refer to the repository's [licensing notice](../README.md#licensing) and [LICENSE.txt](../LICENSE.txt) for terms of use.
