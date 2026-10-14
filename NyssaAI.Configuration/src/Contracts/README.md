# Contracts

Owns the seams an implementer outside this package writes a class against, and the public types
those seams' signatures name. Nothing here is an implementation.

The rule is ownership, not accessibility. A type lives here because someone outside the package has
to *name* it — you cannot write a vault factory without naming `VaultOptions`. A type that
is merely public for the package's own composition does not belong here; it belongs in `Client/` if
a caller names it to construct the package, and inside its feature otherwise. `src/README.md`
states the whole test.

The two root contracts are the exception the architecture tests enforce: `IConfiguration`
and `IConfigurationExtensions` belong at the source root. `PackageStructureTests` preserves the legacy ports here and permits the feature-owned
`CloudKeyVaults/ICloudKeyVaultRecovery` capability. Recovery receipts, handles, and exceptions belong to
`Recovery/`. `PublicTypeInventoryTests` pins the exact exported set.

## Public — implemented or named outside the package

- `ICloudKeyVaultFactory` and `ICloudKeyVault` — a vault package registers the first; the library
  asks it to build the second from a manifest entry. `VaultOptions` is that entry as data
  — name, endpoint, `Settings`, and `Secrets` the library never logs — and `ConfigurationPointer`
  is what a vault returns for a stored value.
- `IAccessTokenSource` — what a vault that signs with OAuth2 takes in its constructor: one call
  that yields a bearer token, with refresh and re-authorization behind it.
- `IKeyring` — the platform credential store. An application or a test registers its own to keep
  the library away from the real one.
- `IBrowserLauncher` — what opens the authorize URI. A test registers one that calls the loopback
  listener itself.
- `ConfigurationRemoval` - returned by `IConfiguration.RemoveAsync` and vault deletion; its
  `RemoteValueRemains` flag includes retained soft-deleted copies.
- `ConfigurationMove` — returned by `IConfiguration.MoveAsync`.

## Internal — the package's own ports

These stay in this folder because they are ports, but nothing outside the package implements them
any more: `IConfigurationProvider` and `ConfigurationValue` (the provider contract and the entry
envelope), `IConfigurationCipher`, `IConfigurationDocumentFactory` and `IConfigurationDocument`,
`IConfigurationValues` (the store port the client is built from),
`IReauthorizableProvider` (the one provider that acquires credentials from a person), and
`OAuthToken` (the self-describing stored credential).
