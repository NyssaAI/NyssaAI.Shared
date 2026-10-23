---
feature: F-101
id: UC-002
status: planned
---

# UC-002 List Keys in a Namespace in Ordinal Order

## Actor

Application Service (`IConfiguration.ListAsync`).

## Goal

Retrieve all configuration key names currently stored in a namespace in deterministic ordinal order, or an empty list when the namespace has no settings.

## Starting Context

The configuration store is initialized. A namespace file (`<namespace>.json`) may contain several keys or may not exist on disk yet.

## Entry Route

Called via `await configuration.ListAsync(namespace, cancellationToken)`.

## Trigger

An application inspects which keys exist in a namespace.

## Process

```text
Actor lists keys in a namespace
+-- Starting context: namespace name supplied
+-- Entry route: IConfiguration.ListAsync(namespace)
+-- Trigger: application requests list of keys
+-- Step 1: Validate namespace name
|   \-- Blank or path-traversal namespace -> Fail with Validation
+-- Step 2: Read <namespace>.json under lock
|   +-- File does not exist -> Return empty list []
|   \-- File exists -> Read entry dictionary keys
+-- Step 3: Sort keys in strict ordinal order
|   \-- Sort by StringComparer.Ordinal (UTF-16 code unit order) without resolving secret values
\-- Success: Read-only list of key names returned in ordinal order
```

## Step Details

### Step 3: Sort keys in strict ordinal order
- `ListAsync` inspects only the local `<namespace>.json` keys; it never decrypts `"aesLocal"` values, refreshes `"oauth"` tokens, or contacts a cloud vault.
- Keys are always sorted using culture-invariant ordinal comparison (`StringComparer.Ordinal`).

## Success

The caller receives a deterministic, ordinal-sorted list of keys in the namespace (or an empty list if the namespace does not exist).

## Failure Cases

- `Validation`: The namespace name is invalid or `<namespace>.json` contains malformed JSON.
- `Forbidden` / `Unavailable`: The configuration directory failed safety verification or the lock timed out.

## Consistency Check

- Unique coverage: Covers `ListAsync(namespace)` key enumeration and ordinal sorting.
- Closest related use cases: `UC-001` reads and writes individual keys; `UC-003` removes keys.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: keys are returned in ordinal order, and a missing namespace returns an empty list without throwing.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
