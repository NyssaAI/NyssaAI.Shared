# Nyssa.Configuration

Use `NyssaAI.Configuration.IConfiguration` to read and write settings by component namespace and key.
Operations return `NyssaAI.Results.Result` or `Result<T>`. Values can be plain JSON entries, encrypted
local entries, OAuth credentials, or pointers to a separately registered vault provider.

## Register the library

```csharp
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;

services.AddNyssaConfiguration();
```

The application name defaults to the entry assembly's standard `AssemblyTitle`, falling back to its
assembly name. Set `options.ApplicationName` to choose a different identity. Inject
`NyssaAI.Configuration.IConfiguration` into consumers within a dependency-injection scope.

The first resolution reads the application's manifest section, prepares its configuration directory,
and obtains or creates the encryption key in the OS credential store. An unavailable keyring is an
initialization failure, including when the application intends to write plain values.

| Setting | Default |
| --- | --- |
| Manifest | `~/.nyssa-ai/config-manifest.json` |
| Data directory for a new application section | `<pwd>/.nyssa-ai/config` |
| Namespace document | `<configurationDirectory>/<namespace>.json` |

An existing manifest section controls the data directory. A relative directory in the manifest is
resolved against the manifest's folder. A missing section at the conventional manifest location is
created using `ConfigDirectory`; invalid or unreadable manifests fail initialization.

Override either location programmatically:

```csharp
services.AddNyssaConfiguration(options =>
{
    options.ApplicationName = "SomeApp-cli";
    options.ConfigDirectory = Path.GetFullPath("application-data/config");
    options.Bootstrap.ConfigManifest = Path.GetFullPath("deployment/config-manifest.json");
});
```

A manifest at a nonconventional path must already exist. Changing the options does not migrate files
or replace an existing application's recorded directory. No environment variable selects these paths.

## Store component DTOs

```csharp
using NyssaAI.Results;
using Configuration = NyssaAI.Configuration.IConfiguration;

// configuration is an injected Configuration; settingsA and settingsB are application-owned DTOs.
var savedA = await configuration.SetAsAsync("A", "settings", settingsA, Configuration.Plain);
savedA.ThrowIfError(); // Explicitly stop if the prerequisite write failed.

var savedB = await configuration.SetAsAsync("B", "settings", settingsB, "aesLocal");
savedB.ThrowIfError();

var loadedA = await configuration.GetAsAsync<ASettings>("A", "settings");
var loadedB = await configuration.GetAsAsync<BSettings>("B", "settings");
```

These operations use `A.json` and `B.json` beneath the directory named by the manifest. A DTO can
include nested objects and collections. Reflection-based typed overloads support ordinary .NET
applications; Native AOT callers supply generated `JsonTypeInfo<T>` to the corresponding overloads.

A new key must name its provider. Existing keys inherit their recorded provider when it is omitted.
Use `MoveAsync` to change providers; `RemoveAsync` removes the local entry and does not delete a
remote vault secret. `ListAsync` lists the keys in one namespace.

## OAuth and vault providers

`IConfigurationExtensions` exposes explicit interactive authorization and reauthorization. Ordinary
reads never open a browser. Stored OAuth credentials refresh through the configured token endpoint.

Provider packages implement `NyssaAI.Configuration.Contracts.ICloudKeyVaultFactory` and
`ICloudKeyVault`. The application's manifest names the provider kind, settings, and authentication
information. Local documents retain provider pointers while remote providers retain the values.
Concrete vault SDKs, including the Bitwarden adapter, are separate packages and are not included in
this initial repository import.

Manifest secrets are bootstrap credentials. Keep them in an owner-restricted file; the library checks
file ownership and permissions and refuses unsafe manifests. The local encryption key belongs in the
OS credential store, independently of the manifest.

## Examples and tests

See the [executable tutorials](docs/tutorials/README.md) for registration, complete DTOs, persistence,
encryption, OAuth, and fake vault providers. From the repository root, run `./run-tests.ps1` to build
and execute the isolated suites. Real OS keyring tests require an explicit opt-in.

The package and assembly are named `Nyssa.Configuration`; their public namespaces start with
`NyssaAI.Configuration`. Refer to the repository's [licensing notice](../README.md#licensing) before use.
