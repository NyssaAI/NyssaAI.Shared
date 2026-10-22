---
feature: F-102
id: UC-004
status: planned
---

# UC-004 Read String Values Without Losing Literal Quotes or Formatting

## Actor

Application Code (`IConfiguration.GetAsAsync<string>`, `GetAsync`).

## Goal

Read string settings correctly so that literal strings containing quotes, numbers, or words like `"null"` are not distorted or mistakenly treated as raw JSON tokens.

## Starting Context

A setting was saved as unencoded text using `SetAsync("server", "tag", "\"quoted\"")`, or as a JSON string using `SetAsAsync<string>`.

## Entry Route

Called via `await configuration.GetAsAsync<string>(namespace, key)` or `await configuration.GetAsync(namespace, key)`.

## Trigger

An application reads a string value and needs the exact original characters.

## Process

```text
Actor reads string configuration setting
+-- Starting context: key exists in <namespace>.json
+-- Entry route: GetAsAsync<string>(namespace, key) or GetAsync(namespace, key)
+-- Trigger: application requests string value
+-- Step 1: Read entry from <namespace>.json
+-- Step 2: Inspect entry metadata flag "json"
|   +-- Case A: Entry has "json": true (written via SetAsAsync<T> or provider-backed JSON)
|   |   \-- Deserialize the stored JSON string (unwrapping outer JSON quotes)
|   \-- Case B: Entry does NOT have "json": true (written via SetAsync unencoded text)
|       \-- Return raw string directly without parsing as JSON
|           \-- Literal text like "123", "null", or "\"quoted\"" retains exact characters
\-- Success: Exact intended string returned to caller
```

## Step Details

### Step 2: Inspect entry metadata flag "json"
- `SetAsync` writes raw text without a `"json": true` marker.
- `SetAsAsync` sets `"json": true` when serializing strings to indicate they were JSON-encoded.
- When calling `GetAsAsync<string>`, the library checks this marker: if it's raw text, it returns it verbatim; if it's marked as JSON, it deserializes the JSON string. This prevents strings like `123` from crashing a typed string read and ensures literal quotes are never lost.

## Success

The application receives the exact string that was saved, without unwanted quote stripping or JSON parse crashes.

## Failure Cases

- `NotFound`: Key is missing from the namespace.
- `Validation`: The entry has `"json": true` but contains malformed JSON.

## Consistency Check

- Unique coverage: Covers string encoding preservation and typed string reads.
- Closest related use cases: `UC-001` and `UC-002` handle general DTO serialization.
- Contradictions: None found.

## Acceptance

- Raw strings containing quotes, numbers, or `null` return their exact literal text.
- JSON-encoded strings are deserialized properly.
