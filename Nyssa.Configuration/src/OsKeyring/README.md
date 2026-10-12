# OsKeyring

Owns access to the operating system credential store, and custody of the key material that must
never reach the configuration directory in the clear. `IKeyring` is the port, reading, writing, and deleting one
secret at a time, alongside the Windows Credential Manager, macOS keychain, Linux Secret Service,
unsupported-platform, address, and default-selection adapters.

Every adapter draws the same three-way distinction: a secret, a success carrying null when nothing
is filed at that address, and a failure when the store could not answer at all. Only one status per
platform means absence -- `ERROR_NOT_FOUND`, `errSecItemNotFound`, or a null lookup with no
`GError` -- so a misread code can never be mistaken for a missing secret.

`KeyringManagedSecret` is the custody helper: it reads secret material from the store, or generates
it in memory and files it there when the store holds none, and returns bytes rather than a cipher so
this feature keeps no opinion about what a key is for. A failed read never creates a replacement —
only a confirmed absence does — because re-keying after a transient outage would orphan everything
sealed under the previous key.

These adapters intentionally remain inside `Nyssa.Configuration`. The composer depends on this
feature for the one secret the package keeps in the credential store: the local encryption key, at
service `<application>`, entry `configuration-key`. The composer calls `GetOrCreate` and logs when it
created the key; a store that cannot answer is a startup failure naming that address, never a
fresh key. The Values store has no dependency on this feature at all.

An application or a test replaces the platform store by registering its own `IKeyring`; the
composer resolves it from the container before falling back to `KeyringDefault`. Deletion uses
`CredDeleteW` on Windows and `SecItemDelete` on macOS. Linux searches all matching items with
`secret_service_search_sync` and deletes each with `secret_item_delete_sync`, including locked
items; a refused deletion is a failure. These synchronous APIs may prompt for access and should run
off a UI thread.

## Running the adapter tests

The Windows, macOS and Linux adapters are covered by a manual suite in
`test/Nyssa.Configuration.Tests/OsKeyring/`. It is deliberately not part of CI: the properties
worth checking here -- an error code mistaken for an absent secret, a blob decoded with the wrong
encoding, a handle released twice -- only appear when a real store answers, and a hosted agent has
no unlocked keyring to give one. A suite that reported "the store is unavailable" on every run
would teach everyone to ignore it.

So it writes to, and deletes from, the credential store of whoever runs it, and stays skipped until
an operator opts in:

```bash
NYSSA_OS_KEYRING_TESTS=1 dotnet run --project test/Nyssa.Configuration.Tests
```

Everything it files goes under the service `nyssa.configuration.tests` with a per-run key and is
deleted whether the test passed or failed. Linux additionally needs `secret-tool`
(`libsecret-tools`) and an unlocked login keyring behind a session bus. Without `secret-tool`,
Linux tests skip; cleanup uses `IKeyring.Delete`. `KeyringStoreTests` holds the contract all three
adapters owe, and each platform class adds the checks only it can make -- that the address and
encoding match what `cmdkey` and `secret-tool` see, and that a store which cannot answer is
reported as a failure rather than as an empty store.

`KeyringMacOsTests` runs the same contract suite against the macOS keychain.
