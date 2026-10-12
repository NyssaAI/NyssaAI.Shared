# Configuration source

Consumers use `NyssaAI.Configuration.IConfiguration` for values and
`NyssaAI.Configuration.IConfigurationExtensions` for interactive authorization. Registration and
startup options live in `Client/`; provider implementers use public contracts in `Contracts/`.

| Directory | Responsibility |
| --- | --- |
| `Client/` | Registration, options, scoped composition, and the consumer implementation. |
| `Contracts/` | Provider interfaces, internal storage ports, and the types their methods exchange. |
| `Bootstrap/` | Manifest parsing, application selection, and first-use provisioning. |
| `Values/` | Namespace documents, provider selection, typed access, and locking. |
| `Encryption/` | Address-bound local encryption and the `aesLocal` provider. |
| `OAuth/` | Authorization, persisted grants, and token refresh. |
| `CloudKeyVaults/` | Adapt remote vault contracts into configuration providers. |
| `OsKeyring/` | Platform credential stores and custody of the local encryption key. |
| `FileSecurity/` | Ownership, permissions, and protected filesystem writes. |

The architecture tests check source-root contracts, public interfaces, and layer dependencies. The
public-type inventory test records the exported API. Concrete vault SDKs live in separate packages.

See the [package README](../README.md) and [tutorials](../docs/tutorials/README.md) for consumer workflows.
