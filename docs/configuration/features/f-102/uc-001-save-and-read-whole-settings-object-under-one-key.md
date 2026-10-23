---
feature: F-102
id: UC-001
status: planned
---

# UC-001 Save and Read Whole Settings Object Under One Key

## Actor

Application Service or Module Developer (`IConfiguration.SetAsAsync<T>`, `GetAsAsync<T>`).

## Goal

Save a complete settings object (like `MailSettings` containing host, port, and retry options) as a single configuration entry under one key, and read it back as a strongly typed object.

## Starting Context

The application is running and has access to `IConfiguration`. A settings object is populated in memory.

## Entry Route

Called via `await configuration.SetAsAsync("mail", "server", mailSettings)` and `await configuration.GetAsAsync<MailSettings>("mail", "server")`.

## Trigger

An application module wants to save or retrieve a cohesive settings object as a single unit under one specific key name.

## Process

```text
Actor saves or reads whole settings object under one key
+-- Starting context: settings object in memory, namespace and key specified
+-- Entry route: SetAsAsync(namespace, key, value) or GetAsAsync(namespace, key)
+-- Trigger: application stores or loads settings
+-- Phase A: Saving Whole Object
|   +-- Caller supplies settings object (e.g. MailSettings) and optional provider
|   +-- Serialize object to JSON
|   +-- If provider is specified (e.g. "aesLocal")
|   |   +-- Encrypt serialized JSON string
|   |   \-- Store in <namespace>.json with "json": true and provider metadata
|   \-- If provider is omitted (plaintext)
|       \-- Store JSON object directly as the "value" of that key
+-- Phase B: Reading Whole Object
|   +-- Read key's entry from <namespace>.json
|   +-- If entry is encrypted or vault-backed -> resolve plaintext JSON string
|   +-- Deserialize JSON string or JSON element into requested type T
\-- Success: Entire settings object stored under one key or deserialized back into memory
```

## Step Details

### Phase A: Saving Whole Object
- The entire object is stored under the single key (e.g. `mail.json` has one entry `"server": { "value": { ... } }`).
- Child properties (like `Host` or `Port`) belong to the object and cannot be addressed as individual configuration keys.
- If encryption is enabled, the entire JSON object is encrypted together under one ciphertext blob.

## Success

The settings object is preserved across process runs under a single configuration key.

## Failure Cases

- `NotFound`: Key does not exist when reading.
- `Validation`: Stored JSON is malformed or cannot be converted into the requested type.

## Consistency Check

- Unique coverage: Covers storing an entire object under a single key.
- Closest related use cases: `UC-002` stores object properties as individual entries in a namespace; `UC-003` updates properties partially.
- Contradictions: None found.

## Acceptance

- Saving an object creates one key entry in the namespace file.
- Reading the key returns a populated, strongly typed object matching the original values.
