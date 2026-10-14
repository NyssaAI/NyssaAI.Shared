# NyssaAI.Configuration

`NyssaAI.Configuration` stores settings and secrets for .NET applications and command-line tools. Application code uses [`IConfiguration`](src/IConfiguration.cs) to read and write values that persist across runs, whether they are stored as plain JSON, encrypted locally, or held in a remote vault. The library is designed to run on Windows, Linux, and macOS, with support for Native AOT compilation.

## Easy setup, consistent access

Supply configuration DTO definitions, register the library, and save or retrieve your configuration DTOs in one call. The library can handle the files, JSON serialization, and encryption by default.

- **Add default configuration.** `AddNyssaConfiguration()` uses the application's assembly identity and default storage paths. On first use, it creates a missing manifest section and prepares the configuration directory for each named configuration package.
- **Keeps each module's settings together.** You can give each module its own namespace and save a complete settings object, including nested objects, with `SetAsAsync("orders", orderSettings)`, and retrieve it with `GetAsAsync<OrderSettings>("orders")`. Alternatively you can specify complex types by property and vary the storage or encryption mechanism by property.
- **Add encryption with a storage choice.** Select `"aesLocal"` when saving a new entry. The library creates and manages the encryption key in the OS credential store; encrypts the data on write and decrypts it on read returning the original DTO unencrypted.
- **Use a remote vault through the same API.** After registering a vault provider and configuring its connection and credentials, use its provider name when saving. The value stays in the vault, and the library keeps a local pointer. The module's read call stays the same. Use `MoveAsync` to change the provider for an existing entry.

Local storage and encryption work out of the box on a supported machine with an available OS credential store. You do not need to choose file paths, generate keys, or prepare a manifest for the default setup.

### Example: two modules using the defaults

Suppose an application has an Orders module and a Notifications module. Each module stores its settings in a separate namespace. Here, `configuration` is an injected `IConfiguration`, and the settings objects are already populated.

```csharp
// Save each module's settings as plain JSON.
await configuration.SetAsAsync("orders", orderSettings);
await configuration.SetAsAsync("notifications", notificationSettings);

// Read the complete settings objects.
var orders = await configuration.GetAsAsync<OrderSettings>("orders");
var notifications = await configuration.GetAsAsync<NotificationSettings>("notifications");
```

Each save replaces its namespace with the DTO's serialized properties, keeping nested objects together. Use `MergeSettingsAsync` for partial updates when omitted entries should remain. Registration prepares the service; the `SetAsAsync` calls supply the values. The same read call assembles the DTO even when some properties use local encryption or a cloud vault.

You can also store a complete DTO under one key, with one provider for the entire object. See [Store a DTO](#store-a-dto) for both call patterns and their JSON layouts.

## IConfiguration - The main interface

Once configuration is registered, inject `IConfiguration` into the classes that need it. The storage choice is recorded with each key, so a database service uses the same call to read a password encrypted on disk or stored in a vault.

```csharp
var host = await configuration.GetAsync("database", "host");
var password = await configuration.GetAsync("database", "password");
```

Application code uses `IConfiguration` to read and write values, regardless of where they are stored.

The API below is organized around `IConfiguration`. Each method accepts an optional cancellation token and returns a `ValueTask`; the tree shows the result after awaiting it.

```text
IConfiguration
|-- GetAsync             string: read one value
|-- SetAsync             new keys use default storage; updates preserve their provider
|-- GetAsAsync<TValue>    TValue: read a named key or the namespace settings
|-- SetAsAsync<TValue>    replace a named value or the namespace contents
|-- MergeSettingsAsync   update supplied namespace properties; preserve omitted entries
|-- RemoveAsync          ConfigurationRemoval: delete and report any retained remote copy
|-- ListAsync            IReadOnlyList<string>: keys in ordinal order
|-- MoveAsync            ConfigurationMove: change provider and report source cleanup
`-- RecoverAsync         ConfigurationRecoveryOutcome: continue safely unfinished work
```

The methods have a few behaviors to account for when using them:

- `GetAsync` throws when the key is missing. Namespace reads also throw when there are no settings to load.
- `ListAsync` returns keys in ordinal order, or an empty list when the namespace does not exist.
- `RemoveAsync` requests deletion through the recorded provider, then removes the local entry. Its `RemoteValueRemains` flag reports provider retention, including soft-deleted copies. An already absent local key succeeds without contacting a provider.
- `GetAsAsync<TValue>`, `SetAsAsync<TValue>`, and `MergeSettingsAsync<TValue>` each have reflection-based and source-generated JSON overloads.
- Keyed `GetAsAsync<string>` reads strings saved through namespace objects, `SetAsync`, or keyed `SetAsAsync`. See [Typed string reads](#typed-string-reads) for the encoding behavior.

These operations are expected to succeed. Reads return the value directly, and writes complete without a return value. Removal and moves return an outcome describing provider cleanup. An unexpected failure throws an exception. Failures reported by the store or a provider throw `ResultException`, which retains the error details; cancellation throws `OperationCanceledException`. There is no `Result` to check or unwrap. Partial move and namespace failures use derived exceptions carrying progress; see [Recover an interrupted operation](#recover-an-interrupted-operation).

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

Each namespace has a JSON file in that folder. A keyed DTO write stores the complete object in one entry. A namespace DTO write gives each top-level property its own entry and storage provider; nested objects remain together within their property's entry. [Store a DTO](#store-a-dto) shows both layouts.

For example, `mailserverSettings.json` can build one DTO from plain settings, a locally encrypted password, and an API key stored in a cloud vault:

```json
{
  "Host": {
    "value": "smtp.example.com"
  },
  "Port": {
    "value": 587
  },
  "UseTls": {
    "value": true
  },
  "Password": {
    "value": "<encrypted password>",
    "encrypted": true,
    "provider": "aesLocal"
  },
  "ApiKey": {
    "value": "mailserverSettings/ApiKey",
    "provider": "mailVault"
  }
}
```

The encrypted password is a placeholder. The pointer `mailserverSettings/ApiKey` uses the Bitwarden Secrets implementation's `namespace/key` format. The adapter finds the secret with that name in the configured organization and retrieves its value. `mailVault` is the configured vault name; it must match the manifest's `keyVault.name`, with the Bitwarden factory registered for kind `bitwarden-secrets`. `encrypted` describes the local value, so it is omitted for a vault pointer.

Entries retain their `value` wrapper and omit metadata at its default: no `provider` means plaintext, and absent `encrypted` or `reauth_required` means false. Nondefault metadata is written when needed. The `"json": true` marker is retained for typed strings and provider-backed JSON so reads preserve their encoding.

Existing files with explicit default fields remain readable; saving a namespace writes the compact form. No migration is required. Omitting a provider in an API update preserves its recorded provider; it does not remove that provider from the file.

`GetAsAsync<MailserverSettings>("mailserverSettings")` resolves all five entries and builds one DTO, including `Password` and `ApiKey`. Entry names match the DTO's serialized property names. If any entry cannot be resolved, the read throws.

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
2. **Read the namespace file.** Find the entry and its recorded provider. A missing entry throws.
3. **Resolve the value.** Return a plain value directly, decrypt a local value, or ask the named vault provider to retrieve the remote secret. For an OAuth entry, obtain the access token and refresh it when needed.
4. **Return the value.** `GetAsync` returns a string. `GetAsAsync<T>` deserializes the value into the requested settings type. An unexpected failure throws.

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
- If a DTO is requested by key, it resolves that entry and deserializes the complete object. If only the namespace is supplied, it resolves the namespace's entries and builds the DTO from those values.

A namespace replacement keeps the providers of included properties, saves new properties as plaintext, and removes omitted local entries. A namespace merge preserves omitted entries. A keyed DTO write uses one provider for the complete object. To change an entry's provider, use `MoveAsync`. The store saves the value at its new location before attempting to remove the old copy.

### Providers

Providers handle the parts of storage that depend on encryption or an external service. Internally, they implement [`IConfigurationProvider`](src/Contracts/IConfigurationProvider.cs):

- **`Name`** identifies the provider recorded with an entry, such as `"aesLocal"` or a configured vault name.
- **`WriteAsync`** stores or encrypts the value and returns the entry to save locally.
- **`ReadAsync`** turns that stored entry back into a usable value.
- **`EraseAsync`** requests provider deletion when a value is explicitly removed or moves elsewhere. It returns `ConfigurationRemoval` with the provider's `RemoteValueRemains` flag. Removing or replacing the local entry completes deletion for locally stored encrypted data.

The built-in providers handle local encryption and OAuth credentials, including token refresh. Plain values are handled directly by the configuration store.

Vault packages implement the public `ICloudKeyVaultFactory` and `ICloudKeyVault` contracts. The library wraps the vault in an internal provider and connects it to the store. Application code continues to call `IConfiguration`; it does not call the vault adapter directly. See [Connect a remote vault](#connect-a-remote-vault) for the provider setup.

## Working with settings


### Recover an interrupted operation

Moves and namespace `SetAsAsync`/`MergeSettingsAsync` calls can finish only partly. A vault may commit a write before the connection drops, or local publication may fail after remote writes succeed. The library captures that progress and, when safe continuation is supported, supplies a protected recovery handle.

**Application code does not implement a callback.** It decides whether and when to call `RecoverAsync(handle)`. The library coordinates the unfinished work; the vault adapter supplies the backend evidence needed to do it safely.

A move can succeed at its destination while source cleanup remains unfinished:

```csharp
using NyssaAI.Configuration.Recovery;

var moved = await configuration.MoveAsync("mail", "password", "aesLocal");

// The destination is ready. Inspect Progress for cleanup failure versus retention.
ConfigurationRecoveryHandle? handle = moved.RecoveryHandle;

// When the application chooses to retry:
if (handle is not null)
{
    var outcome = await configuration.RecoverAsync(handle);
    handle = outcome.RecoveryHandle; // Replace the saved handle if work remains.
}
```

A failed namespace write throws `ConfigurationOperationException`, which derives from `ResultException`:

```csharp
ConfigurationRecoveryHandle? handle = null;
try
{
    await configuration.SetAsAsync("mail", mailSettings);
}
catch (ConfigurationOperationException failure)
{
    var progress = failure.Progress; // Payload-free diagnostics.
    handle = failure.RecoveryHandle; // Null when automatic recovery is unavailable.
    // Report progress.Recovery and progress.Steps to the application's operator.
}

// Later, after the application decides to continue:
if (handle is not null)
{
    var outcome = await configuration.RecoverAsync(handle);
    // Completed, Unfinished, Conflict, or ManualReconciliation.
    var status = outcome.Status;
    handle = outcome.RecoveryHandle;
}
```

Cancellation after work starts throws `ConfigurationOperationCanceledException`, derived from `OperationCanceledException`. It carries the same `Progress` and `RecoveryHandle` properties and preserves the caller's token. Use a fresh cancellation token for a later recovery attempt. Existing catches for the base exception types still work.

`Progress` separates remote effects from local publication:

| Field | Meaning |
| --- | --- |
| `OperationId`, `Kind`, `Namespace` | The original move, replacement, or merge. |
| `Steps` | Each key, provider, stable request ID, action, state, and optional safe target reference. |
| `Publication` | `NotAttempted`, `Published`, `Failed`, or `Unknown` for the local namespace. |
| `Recovery` | `Completed`, `Unfinished`, `Conflict`, or `ManualReconciliation`. |

A step can be `Pending`, `Applied`, `Failed` (confirmed no effect), `Unknown`, `Conflict`, `Retained`, or `Manual`. For example, if A succeeds, B commits but its response is lost, and C never starts, the receipt reports **A: Applied, B: Unknown, C: Pending**. Recovery reconciles B, reuses confirmed outputs, completes the safe remaining writes, and publishes the original intended namespace. It does not resubmit the DTO or repeat serialization.

After a move publishes its destination, recovery addresses only its recorded source cleanup. It never deletes through the entry's current provider. Changed local configuration or provider identity stops recovery with a conflict. Remote writes and deletions require atomic provider preconditions. Provider retention is terminal: it does not trigger retries or purging.

To retain a handle across application restarts, explicitly export and import it:

```csharp
string savedToken = handle.Export(); // Persist through application-owned storage.
var restored = ConfigurationRecoveryHandle.Import(savedToken);
var outcome = await configuration.RecoverAsync(restored);
```

Exported handles are encrypted and authenticated using a separate purpose key derived from the application's existing protection key. They are bound to the application and canonical configuration root. Import only constructs a handle; `RecoverAsync` authenticates it before contacting providers. Keep tokens private and do not log them. Automatic formatting is redacted. Changing or losing the protection key invalidates saved handles.

The library does not save a journal or run background retries. A crash before the caller receives and saves a handle cannot be resumed through this API. This recovery contract covers moves and namespace writes; ordinary keyed writes and explicit `RemoveAsync` retain their existing contracts. Namespace JSON files do not gain recovery metadata.

#### Implement recovery in a vault adapter

Implement [`ICloudKeyVaultRecovery`](src/CloudKeyVaults/ICloudKeyVaultRecovery.cs) on the same object that implements `ICloudKeyVault`. Its existing factory registration is sufficient. Existing adapters continue working without this capability; partial failures then report manual reconciliation and no automatic recovery handle.

| Capability | Adapter responsibility |
| --- | --- |
| `StoreIdentity` | Identify the actual account, tenant, and store, independently of the registration name. Keep it stable for the adapter's lifetime. |
| `PrepareAsync(operation)` | Read-only capture of the exact target and concurrency condition before the first mutation. Return protected opaque context and an optional non-secret target reference. |
| `ExecuteAsync(operation, preparation)` | Atomically enforce the condition and deduplicate the stable operation ID against the identical frozen intent. Return the original pointer for a completed write. |
| `ReconcileAsync(operation, preparation)` | Read-only determination of whether the original request completed and whether its output or pending condition is still valid. |

These methods return `Result<CloudKeyVaultPreparation>` or `Result<CloudKeyVaultEffect>` inside a `ValueTask`. The library uses them on the original attempt as well as during recovery.

Return `Applied` only with evidence of the original operation; matching content is insufficient. Return `Failed` only when no effect occurred and the prepared retry condition remains valid. A failed Result or thrown exception from a dispatched mutation is treated as `Unknown`. Return `Conflict` when a newer value prevents continuation, `Manual` when evidence has expired or backend guarantees are insufficient, and `Retained` for acknowledged deletion with a retained copy. `Pending` is a library state, not a provider response.

Request deduplication must survive adapter recreation and concurrent callers. Applied write pointers must still identify the original output when reconciled. A read followed by an unguarded write or delete is insufficient: the mutation itself must enforce the condition atomically. Backend limitations cannot be repaired by an in-memory retry cache. Preparation contexts must not contain access tokens, refresh tokens, or reusable credentials; errors and diagnostic target references must contain no setting values or secret material.

See the runnable [recovery tutorials](../test/NyssaAI.Configuration.UseCaseTests/ConfigurationRecoveryTutorials.cs) and [consumer-implemented recovery vault](../test/NyssaAI.Configuration.UseCaseTests/fixtures/RecoveryVaultFactory.cs). That fixture models backend request records and revisions; a production adapter must obtain equivalent guarantees from its remote system.

### Remove a value

`RemoveAsync` requests deletion from the entry's provider and then removes the local entry. It returns the provider's retention status:

```csharp
var removal = await configuration.RemoveAsync("mail", "Password");
bool remoteValueRemains = removal.RemoteValueRemains;
```

- `false`: the provider reports no remaining remote copy, or the value was stored only locally.
- `true`: the local entry is gone, but the provider retains a remote copy. This includes recoverable soft-deleted secrets and stores that do not support deletion.

For example, Azure Key Vault can retain a recoverable secret after soft deletion until it is purged or its retention period expires. Its adapter must report `true` while that copy remains. See [Microsoft's soft-delete documentation](https://learn.microsoft.com/azure/key-vault/general/soft-delete-change).

An operational failure, such as a vault outage or denied permission, throws and preserves the local entry for retry. If remote deletion succeeds but saving the local document fails, the local pointer remains; retrying deletion is safe because providers must handle already absent secrets. A provider must continue reporting `true` for a retained soft-deleted copy on retry.

Removing an already absent local key returns `RemoteValueRemains = false` without contacting a provider. That retry cannot discover remote copies left by a previous successful removal, so retain the original outcome if cleanup matters.

`MoveAsync` uses the same provider deletion outcome: `SourceWithdrawn` is false when the source provider retains a copy, including after soft deletion, or when source cleanup fails.

**Updating callers and vault adapters:** ordinary `await configuration.RemoveAsync(...)` calls can continue ignoring the outcome. Callers that need cleanup status can capture the returned `ConfigurationRemoval`. Vault implementations must update `ICloudKeyVault.RemoveAsync` to return `ValueTask<Result<ConfigurationRemoval>>` and explicitly report `RemoteValueRemains`. Operational failures remain failed results.

### Replace or merge settings

`SetAsAsync` always replaces the selected value or namespace. `MergeSettingsAsync` is the explicit operation for partial namespace updates.

| Call | What it replaces | What happens to omitted data |
| --- | --- | --- |
| `SetAsAsync(namespace, key, value)` | The complete value at one key. | Omitted object members disappear from that value; other namespace entries remain. |
| `SetAsAsync(namespace, settings)` | All entries in that namespace. | Keys absent from the serialized object are removed locally. |
| `MergeSettingsAsync(namespace, settings)` | Only the supplied top-level property values. | Omitted namespace keys remain unchanged. |

```csharp
await configuration.SetAsAsync("mail",
    new { Host = "smtp.old.test", Port = 587 });

await configuration.MergeSettingsAsync("mail",
    new { Host = "smtp.new.test" });
// Host changes; Port remains 587.

await configuration.SetAsAsync("mail",
    new { Host = "smtp.final.test" });
// The namespace now contains only Host. Port has been removed.
```

Both namespace operations preserve the providers of included keys and use default storage for new keys. Namespace replacement removes omitted entries locally and leaves remote vault secrets in place. To request provider deletion, call `RemoveAsync` on those keys before replacing the namespace. A failed local write does not publish a partial namespace, but remote updates across providers are not atomic.

Merging is shallow: supplying `Retry = new { Attempts = 3 }` replaces the entire `Retry` value, including any previous nested properties. A serialized null is stored as a value. Only properties actually emitted by the serializer are supplied; default-valued DTO properties still participate unless serialization omits them.

An empty object clears the namespace with `SetAsAsync` and leaves its settings unchanged with `MergeSettingsAsync`. Reading an empty namespace still throws.

**Updating existing callers:** namespace `SetAsAsync` previously merged properties. Change those calls to `MergeSettingsAsync` if they must preserve omitted entries. Keep `SetAsAsync` when the supplied object is the complete namespace snapshot. Keyed writes and stored file formats do not change.

### Store a DTO

Choose the storage shape by supplying or omitting a key:

| Shape | Entries in the namespace file | Use when |
| --- | --- | --- |
| Whole DTO under one key | One entry containing the complete object. | The object should be read, replaced, and protected as a unit. |
| DTO properties in a namespace | One entry per top-level serialized property. | Properties need individual access or different storage providers. |

The examples below show alternative layouts for a new `mail` namespace. In both, `mailSettings` is a populated `MailSettings` DTO with `Host`, `Port`, and a nested `Retry` object. The JSON shows the example values.

#### Store the whole DTO under one key

```csharp
await configuration.SetAsAsync(
    "mail", "server", mailSettings);

var settings = await configuration.GetAsAsync<MailSettings>("mail", "server");
```

The complete DTO is the value of the `server` entry in `mail.json`:

```json
{
  "server": {
    "value": {
      "Host": "smtp.example.test",
      "Port": 587,
      "Retry": {
        "Attempts": 3,
        "DelaySeconds": 5
      }
    }
  }
}
```

`server` is the configuration key. `Host`, `Port`, and `Retry` belong to the stored object and cannot be read as separate configuration keys. Read the DTO to access them. A subsequent keyed write replaces the value of `server`.

The complete object uses one provider. Omitting the provider uses default storage, which is plaintext for a new entry. To protect a new DTO containing credentials, pass `"aesLocal"` or a configured vault name instead. Later writes can omit the provider to inherit it; use `MoveAsync` to change it. With encryption, the entry holds ciphertext; with a vault, it holds a pointer to the complete serialized DTO.

#### Store top-level properties in a namespace

Omit the key to give each top-level property its own entry:

```csharp
await configuration.SetAsAsync("mail", mailSettings);
var settings = await configuration.GetAsAsync<MailSettings>("mail");
```

For a new namespace, `mail.json` contains:

```json
{
  "Host": {
    "value": "smtp.example.test"
  },
  "Port": {
    "value": 587
  },
  "Retry": {
    "value": {
      "Attempts": 3,
      "DelaySeconds": 5
    }
  }
}
```

Entry keys match the DTO's serialized property names. `Retry` stays together as a nested object. You can read either the complete DTO or an individual top-level property:

```csharp
var host = await configuration.GetAsAsync<string>("mail", "Host");
var retry = await configuration.GetAsAsync<RetrySettings>("mail", "Retry");
```

Namespace `SetAsAsync` replaces the namespace contents. Included keys preserve their providers, new keys use plaintext, and omitted keys are removed locally. Use `MergeSettingsAsync` when omitted keys should remain. Namespace reads resolve the entries and assemble the DTO; an empty namespace throws.

Use `SetAsync` to create a string property's entry with a chosen provider before the first namespace save, or `MoveAsync` to change an existing property's provider. Later writes preserve those choices for included properties. Remote writes are not atomic across providers: a later failure can leave earlier remote updates in place.

Older files containing a `namespaceSettings` entry remain readable. A namespace `SetAsAsync` replaces a plaintext legacy object with the supplied property entries; `MergeSettingsAsync` first expands that object and preserves its omitted properties. A protected legacy object throws a `ResultException` with the `Conflict` error category on migration; explicitly move it to plaintext first and choose providers for its properties. Non-object namespace values retain their legacy single-entry format, replacing other entries. `MergeSettingsAsync` requires object-shaped input and rejects non-object legacy settings.

#### Typed string reads

`GetAsAsync<string>("mail", "Host")` reads string properties written by a namespace DTO save, unencoded text written with `SetAsync`, and JSON-encoded strings written with keyed `SetAsAsync`. Unencoded text such as `123`, `null`, or text containing literal quotes retains its exact text.

The reader uses the entry's stored JSON metadata to distinguish unencoded text from JSON strings. Both typed read overloads follow this rule, including when the caller supplies a custom string converter. Invalid JSON-marked values still throw, and other requested types continue to deserialize the stored text as JSON. The storage format requires no migration.

`GetAsync` returns the raw resolved text. That text can include JSON quotes when the value was written with keyed `SetAsAsync<string>`; use `GetAsAsync<string>` to obtain the deserialized string.

#### Native AOT and trimming

For Native AOT or trimmed applications, pass a source-generated `JsonTypeInfo<MailSettings>` from your application's JSON context. The keyed write places `typeInfo` after the value:

```csharp
await configuration.SetAsAsync(
    "mail", "server", mailSettings, typeInfo);
var settings = await configuration.GetAsAsync("mail", "server", typeInfo);
```

For the namespace layout, the write places `typeInfo` before the value:

```csharp
await configuration.SetAsAsync("mail", typeInfo, mailSettings);
var settings = await configuration.GetAsAsync("mail", typeInfo);

// Merge uses the same namespace/type-info/value order.
await configuration.MergeSettingsAsync("mail", typeInfo, mailSettings);
```

The reflection-based overloads carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` annotations.

### Choose where a value is stored

New keys use plaintext by default. Supply a provider name when a new value needs encryption or a remote vault. Later writes preserve the recorded storage choice when the provider is omitted.

| Provider | Storage |
| --- | --- |
| Omitted for a new entry | Plain JSON in the local namespace file. |
| `"aesLocal"` | Local AES-256-GCM encryption, with the encryption key held in the OS credential store. |
| A configured vault name | The secret stays in the remote vault; the local file holds a pointer to it. |

```csharp
// Create a plain setting.
await configuration.SetAsync("server", "port", "8080");

// Create an encrypted secret. secretKey comes from the caller.
await configuration.SetAsync("server", "apiKey", secretKey, "aesLocal");

// Update the secret using its recorded provider.
await configuration.SetAsync("server", "apiKey", newSecretKey);

// Move the port setting from plain storage to local encryption.
var moved = await configuration.MoveAsync("server", "port", "aesLocal");

// Return the setting to default storage.
var returned = await configuration.MoveAsync("server", "port");
```

For the overloads that take an explicit key, the library selects the provider as follows:

1. A new key uses default storage when the provider is omitted or null. The default is plaintext.
2. For an existing key, omitting `provider` or passing `null` keeps the current provider.
3. Passing the same provider is allowed. Passing a different one to `SetAsync` throws.
4. Use `MoveAsync` to change providers. Omit its destination provider to return to default storage. Moving to the current destination throws.

A move makes the value available through the new provider, then tries to remove the old copy. Check [`ConfigurationMove.SourceWithdrawn`](src/Contracts/ConfigurationMove.cs) after the move. If it is `false`, you can use the value at its destination, but a copy remains at the source. The library also logs a warning when this happens.

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

Use [`IConfigurationExtensions`](src/IConfigurationExtensions.cs) for setup commands that ask the user to sign in through a browser. Ordinary services use `IConfiguration`. If a read needs sign-in, it throws so your application can decide when to involve the user. The interactive methods return `Result` because the user can decline or cancel sign-in.

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

If the server rejects the refresh grant, the library marks the entry as requiring reauthorization. Reads then throw `ResultException` with `ErrorCategory.Forbidden` without opening a browser. Call `ReauthorizeAsync` from an interactive command to obtain a new grant.

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
- Coordinating file access across processes and throwing when configuration operations fail.

An application can start with the defaults and select different paths, providers, or bootstrap settings as its needs change.

## Run the tutorials and tests

The [executable tutorials](docs/tutorials/README.md) show how to use the public API. They use isolated storage and fake external services so you can explore the behavior through tests.

| Tutorial | What it covers |
| --- | --- |
| [ManifestTutorials](../test/NyssaAI.Configuration.UseCaseTests/ManifestTutorials.cs) | First-use setup, paths, overrides, and manifest precedence. |
| [SetupTutorials](../test/NyssaAI.Configuration.UseCaseTests/SetupTutorials.cs) | Application identity, startup failures, encryption, and vault setup. |
| [ConfigurationTutorials](../test/NyssaAI.Configuration.UseCaseTests/ConfigurationTutorials.cs) | Plain and encrypted values, nested objects, persistence across scopes, provider moves, and source-generated JSON. |
| [ConfigurationDefaultsTutorials](../test/NyssaAI.Configuration.UseCaseTests/ConfigurationDefaultsTutorials.cs) | Default DTO storage, updates that preserve providers, moves back to default storage, and compact JSON. |
| [NamespaceWriteTutorials](../test/NyssaAI.Configuration.UseCaseTests/NamespaceWriteTutorials.cs) | Replacement versus merging, omitted keys, nested objects, nulls, and empty objects. |
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
