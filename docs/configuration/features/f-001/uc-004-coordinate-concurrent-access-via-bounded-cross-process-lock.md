---
feature: F-001
id: UC-004
status: planned
---

# UC-004 Coordinate Concurrent Access via Bounded Cross-Process Lock

## Actor

Namespace Document Store / Manifest Writer (`ConfigurationDocumentLocalFile`, `ConfigurationManifestWriter`).

## Goal

Serialize concurrent read and write operations across multiple threads and operating system processes using an exclusive `.lock` file with exponential backoff and bounded timeouts, preventing race conditions and torn state while avoiding indefinite hangs.

## Starting Context

Multiple processes or threads within an application wish to read from or modify the same namespace file (`<namespace>.json`) or manifest file (`config-manifest.json`).

## Entry Route

Invoked via `AcquireLockAsync` or synchronous `AcquireLock` in `ConfigurationDocumentLocalFile` or `ConfigurationManifestWriter` when beginning a read or mutation.

## Trigger

A caller calls `ReadAsync`, `ModifyAsync`, or `SetConfigurationDirectory`.

## Process

```text
Actor acquires exclusive bounded lock
+-- Starting context: target file path and lock timeout (default 10s)
+-- Entry route: AcquireLock / AcquireLockAsync called
+-- Trigger: document read, modify, or manifest update requested
+-- Step 1: Thread-level serialization (in-process gate)
|   +-- Acquire in-process SemaphoreSlim(1, 1) with cancellation support
|   \-- Gate wait canceled -> Return Result.Failure(Canceled)
+-- Step 2: Validate lock directory
|   +-- Call ProtectedFileSystem.CreateDirectory(parentOfLockPath)
|   \-- Directory creation fails -> Release gate, return Result.Failure(DirectoryNotCreatable)
+-- Step 3: Cross-process lock loop (with exponential backoff)
|   +-- Start timestamp recorded; initial backoff = 5ms
|   +-- Attempt exclusive file open:
|   |   +-- Path: <targetPath>.lock
|   |   +-- Mode: OpenOrCreate
|   |   +-- Access: ReadWrite
|   |   \-- Share: None (exclusive lock handle)
|   +-- Attempt succeeds:
|   |   \-- Return Result.Success(FileStream) -> Hold handle during critical section
|   \-- Attempt fails with IOException (lock held by another process):
|       +-- Check elapsed time against lockTimeout
|       |   +-- Elapsed >= lockTimeout
|       |   |   \-- Release gate, return Result.Failure(LockNotAcquired) [Unavailable]
|       |   \-- Elapsed < lockTimeout -> Compute remaining wait
|       +-- Delay for min(backoff, remainingTimeout)
|       +-- Double backoff (up to 50ms cap)
|       \-- Loop back to Step 3
+-- Step 4: Execute protected critical section
|   \-- Caller performs read or modify/publish under lock
\-- Step 5: Release and cleanup
    +-- Dispose FileStream (OS automatically releases exclusive lock on file)
    \-- Release SemaphoreSlim in finally block
```

## Step Details

### Step 3: Cross-process lock loop
- **Kernel-Level Lock Semantics:** Opening `<path>.lock` with `FileShare.None` creates an exclusive lock handle in the OS kernel. If a process holding this handle terminates abruptly or crashes, the operating system kernel closes the file handle immediately, ensuring that no stale lock files block subsequent processes.
- **Exponential Backoff:** The retry delay starts at 5 milliseconds and doubles on each contention collision up to a maximum of 50 milliseconds (`5ms -> 10ms -> 20ms -> 40ms -> 50ms`). The sleep or `Task.Delay` duration is explicitly clamped to `_lockTimeout - elapsed` so the operation times out exactly at the deadline.
- **Timeout and Cancellation:** If the lock cannot be obtained within `lockTimeout` (default 10 seconds), it returns `LockNotAcquired` with `ErrorCategory.Unavailable`, signaling to the caller that the store is temporarily busy and safe to retry. CancellationToken interrupts immediately abort the loop.

## Success

The calling thread and process obtain exclusive access to the target configuration resource for the duration of the critical section, serializing all mutations without data corruption.

## Failure Cases

- `LockTimeoutInvalid`: The caller supplied a zero or negative `lockTimeout` (`ErrorCategory.Validation`).
- `LockNotAcquired`: The lock was contested and could not be acquired within the allocated timeout (`ErrorCategory.Unavailable`).
- `Canceled`: The operation was cancelled before or during lock acquisition.
- `NotWritable`: UnauthorizedAccessException or permission failure creating or opening the `.lock` file.

## Consistency Check

- Unique coverage: Covers concurrency coordination across multiple threads and processes.
- Closest related use cases: `UC-003` performs the actual file write under the protection of this lock.
- Contradictions: None found.

## Acceptance

- Concurrent processes attempting to update the same document serialize cleanly without throwing file access collisions.
- When a lock is held longer than `lockTimeout`, waiting callers fail with `ErrorCategory.Unavailable` naming the contested file and wait duration.
- Process crashes while holding a lock do not permanently lock out subsequent callers.
