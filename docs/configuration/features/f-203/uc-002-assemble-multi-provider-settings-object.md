---
feature: F-203
id: UC-002
status: planned
---

# UC-002 Assemble a Multi-Provider Settings Object

## Actor

Application Module (`IConfiguration.GetAsAsync<T>(namespace)`, `SetAsAsync<T>(namespace, value)`).

## Goal

Load and update a single strongly typed settings object whose individual properties are split across plaintext JSON, local AES-256-GCM encryption (`"aesLocal"`), and a remote cloud key vault.

## Starting Context

A namespace file (such as `mailserverSettings.json`) has plain properties (`Host`, `Port`, `UseTls`), a locally encrypted `Password` (`provider: "aesLocal"`), and a vault-backed `ApiKey` (`provider: "mailVault"`).

## Entry Route

Called via `var settings = await configuration.GetAsAsync<MailserverSettings>("mailserverSettings")` and `await configuration.SetAsAsync("mailserverSettings", updatedSettings)`.

## Trigger

An application module reads or saves its settings DTO without wanting to manage separate APIs for plain settings, local secrets, and cloud vault secrets.

## Process

```text
Actor reads and updates a DTO spanning plaintext, aesLocal, and a cloud vault
+-- Starting context: mailserverSettings.json has entries using plaintext, "aesLocal", and "mailVault"
+-- Entry route: GetAsAsync<MailserverSettings>("mailserverSettings") or SetAsAsync
+-- Trigger: application module loads or saves its settings DTO
+-- Phase A: Assembling the DTO on Read (GetAsAsync<T>)
|   +-- Read all entries from mailserverSettings.json under lock
|   +-- Resolve each property through its own recorded provider:
|   |   +-- Host, Port, UseTls -> Read directly from local JSON
|   |   +-- Password -> Decrypt locally via "aesLocal" cipher (F-003)
|   |   \-- ApiKey -> Fetch secret from "mailVault" using stored ConfigurationPointer (UC-001)
|   +-- Any property fails to resolve -> Fail read and throw ResultException
|   \-- All properties resolved -> Deserialize combined JSON object into MailserverSettings
+-- Phase B: Updating the DTO on Write (SetAsAsync / MergeSettingsAsync)
|   +-- Decompose updated DTO into top-level properties
|   +-- Preserve each existing property's provider:
|   |   +-- Host, Port, UseTls -> Saved as plain JSON
|   |   +-- Password -> Re-encrypted with "aesLocal"
|   |   \-- ApiKey -> Written to "mailVault" and pointer saved in mailserverSettings.json
|   \-- Publish updated mailserverSettings.json atomically
\-- Success: Application works with one cohesive DTO while each property stays in its chosen store
```

## Step Details

### Phase A & Phase B: Provider Transparency
- The consumer class (`MailService`) never names `"aesLocal"` or `"mailVault"` when reading or updating `MailserverSettings`. Once a property's provider is established (via initial `SetAsync` or `MoveAsync`), `GetAsAsync<T>` and `SetAsAsync<T>` / `MergeSettingsAsync<T>` automatically route each property through its recorded provider.

## Success

`GetAsAsync<T>(namespace)` returns a single populated DTO assembled from plaintext, local encryption, and the remote cloud vault, and subsequent namespace writes preserve each property's provider.

## Failure Cases

- `NotFound` / `Unavailable` / `Forbidden`: Any single property in the namespace fails to decrypt or cannot be retrieved from the cloud vault.
- `ConfigurationOperationException`: A multi-property namespace write fails mid-flight after updating the cloud vault (handled by `F-204`).

## Consistency Check

- Unique coverage: Covers assembling and updating a multi-provider namespace DTO combining plaintext, `"aesLocal"`, and cloud vault entries.
- Closest related use cases: `UC-001` covers single-key vault operations; `F-102 UC-002` covers general namespace DTO replacement.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: one `GetAsAsync<T>(namespace)` call resolves plaintext, `"aesLocal"`, and cloud vault properties into a single DTO.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
