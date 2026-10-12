# CloudKeyVaults

Owns resolution of configuration entries whose value lives in a remote secret store rather than in
the configuration document. `ICloudKeyVault` in `Contracts/` is the port a vault package
implements; this feature owns the adapter that turns one into a provider, so a vault is written
once against three members and never against the entry model.

The store holds the value; the entry holds a `ConfigurationPointer`. That is the whole shape, and
it is what makes `Remove` local-only — dropping the entry leaves the secret where it is.

## Concrete vaults are separate packages

A vault ships as its own package with its own dependencies. An in-package implementation would put
its SDK and native assets in front of every consumer of `Nyssa.Configuration`, used or not, and
would put someone else's dependency inside this package's AOT guarantee.

The package registers an `ICloudKeyVaultFactory` declaring its `Kind`, through one registration
method in the fluent convention — `services.AddContosoVault()`, which does `TryAddEnumerable` so a
second call is harmless and the application never names the factory type. The application's
manifest section names the vault by provider name and kind; the composer matches the two and hands
the factory the entry as `VaultOptions`. Selection is a dictionary lookup over what was
compiled in, never a load by name, which keeps provider selection compatible with Native AOT.
The current manifest schema selects one vault per application section.

## Authentication

A vault that signs its requests with OAuth2 takes an `IAccessTokenSource` in its constructor and
returns its authorization server from `Authorization(settings)`. The manifest's `credential` names
the entry the vault authenticates with — an ordinary sealed `oauth` entry in local storage.
`AccessTokenSource` here is what the package receives: one call that reads that entry through the
store, so refresh, rotation, locking, and re-authorization happen without the package knowing any of
it exists. It never holds a refresh token, a cipher, or a key.

A vault on a host where nobody can sign in authenticates with the entry's `secrets` instead — a
client secret, an API key — delivered on `VaultOptions.Secrets` as string pairs the
library never interprets, logs, or repeats. A package must not either.

A provider's credential may never resolve through that provider. The store refuses the write that
would put it there, and the token source refuses the read that would find it there, both as
`CircularCredential` and both before anything reaches the vault.

The feature has no covering contract of its own: the composer builds the adapter directly.
