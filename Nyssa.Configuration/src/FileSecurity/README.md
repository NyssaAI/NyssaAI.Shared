# File Security

Owns the shared filesystem protection primitives required by Bootstrap and Values.
`DirectorySafety` validates ownership and unsafe write access, while `ProtectedFileSystem` creates
directories and files with restrictive permissions and publishes protected content.

This is a deliberately narrow supporting capability, not a generic utilities folder. It owns only
the security boundary shared by manifest persistence and configuration-document persistence. `VerifyPrivateFile` adds the read bits for a manifest that carries secrets.

## Why other features call these directly

Both types are stateless static primitives, not adapters behind a port, so the rule that a feature
may not name another feature's implementation does not apply: there is no contract here to depend on
instead. That is deliberate. `DirectorySafety` is a security control, and making it substitutable
would mean a caller could supply one that verifies nothing -- a test double that escapes into a
composition would silently disable the check. It is not injectable so that it cannot be replaced.
