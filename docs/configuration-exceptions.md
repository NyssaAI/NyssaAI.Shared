# Configuration operations return values directly

`IConfiguration` operations are expected to succeed. A missing required setting, invalid JSON, an unavailable provider, or a failed write is an exception for the caller, so this interface no longer returns `Result`.

- Reads return `ValueTask<T>`. Writes and removal return `ValueTask`. `MoveAsync` returns `ValueTask<ConfigurationMove>`.
- `ConfigurationClient` translates failures from the internal store into `ResultException`, preserving the existing error details. Reusing this exception avoids adding another public exception with the same purpose. Callers do not need to check or unwrap results.
- Cancellation throws `OperationCanceledException` with the caller's token. A request that is already canceled stops before the store is called.
- Empty listings and removing an absent key remain successful operations. Missing settings still fail; reads do not create default values.
- A completed move can report `SourceWithdrawn = false` when the new location is ready but cleanup at the old location failed. Keeping that information prevents callers from treating the move as though nothing changed.

The internal store and provider contracts retain their existing result handling. Interactive OAuth setup also keeps `Result` because consent can be declined or canceled. Repository configuration extensions retain their own validation results and convert configuration exceptions into sanitized errors at that boundary.

This deliberately overrides the general Result convention in the development standards for `IConfiguration`. It is a breaking API change: remove `.ThrowIfError()`, `.Value`, and result checks from ordinary configuration calls. No package version changes are included in this correction.
