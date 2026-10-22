---
feature: F-003
id: UC-004
status: planned
---

# UC-004 Remove Master Key from Keychain

## Actor

System Administrator, Uninstallation Script, or Test Suite Tear-Down (`IKeyring.Delete`).

## Goal

Explicitly delete the application's master encryption key from the operating system keychain when uninstalling the application or cleaning up after automated test runs.

## Starting Context

An application's key is filed in the OS credential store under `(<ApplicationName>, "configuration-key")`.

## Entry Route

Invoked through administrative commands, uninstaller routines, or integration test teardown hooks.

## Trigger

An explicit request to remove the application's credentials from the host machine.

## Process

```text
Actor removes application key from OS keychain
+-- Starting context: application identity known
+-- Entry route: IKeyring.Delete(service, key) invoked
+-- Trigger: application uninstall or test cleanup
+-- Step 1: Request deletion from platform credential store
|   +-- Windows: calls CredDeleteW
|   +-- macOS: calls SecItemDelete
|   \-- Linux: calls secret_item_delete_sync
+-- Step 2: Evaluate deletion outcome
|   +-- Key found and deleted successfully -> Return Result.Success
|   +-- Key was already absent (not found) -> Return Result.Success (idempotent)
|   \-- Store refused deletion or reported OS error -> Return Result.Failure(StoreNotDeletable)
\-- Success: Master key removed from OS keychain
```

## Step Details

### Step 2: Evaluate deletion outcome
- **Idempotent Completion:** If the key is already gone (`ERROR_NOT_FOUND` / `errSecItemNotFound`), the call succeeds so cleanup scripts and uninstallers do not fail when run repeatedly.
- **Platform Error Handling:** If the platform security subsystem refuses deletion (for example, if permissions are restricted or the keychain is locked), the error is surfaced cleanly as `StoreNotDeletable`.

## Success

The application's master key is completely removed from the OS keychain, ensuring no residual cryptographic keys remain on the machine.

## Failure Cases

- `StoreNotDeletable`: The operating system credential manager returned an error refusing to delete the item.

## Consistency Check

- Unique coverage: Covers deleting the master key from the OS keychain.
- Closest related use cases: `UC-001` creates or reads the key.
- Contradictions: None found.

## Acceptance

- The credential entry is deleted from the OS credential store.
- Subsequent calls to read the key report that it is absent.
- Calling delete when the key is already absent succeeds without error.
