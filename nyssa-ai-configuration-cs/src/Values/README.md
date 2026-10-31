# Values

Owns the caller-facing configuration store and the document model that persists one namespace of
entries. `IConfigurationValues` is the primary port; `ConfigurationStore` coordinates literal and
provider-backed reads and writes.

`ConfigurationValue` is the stored shape: a value, the provider that resolves it, whether it is sealed, and
whether the provider needs re-authorization. An entry naming no provider is a literal. `ConfigurationPointer`
is the address form a provider-backed value references.

Persistence is JSON documents with namespace locking and atomic publication. This feature depends on
`Contracts` for value resolution and `FileSecurity` for protected filesystem access; it owns no
encryption, OAuth, vault, or bootstrap behaviour.

`IValues` is this feature's covering contract: siblings and the client area open documents and the entry store through it, never through `ConfigurationStore` or `ConfigurationDocumentFactoryLocalFile`.
