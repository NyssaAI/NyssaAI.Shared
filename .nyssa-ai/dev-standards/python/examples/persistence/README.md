# PostgreSQL registration example

> [!IMPORTANT] Scope Directive
> Persistence and database integration reference examples for PYTHON in dev-standards.

This small workflow registers one normalized email address, rejects duplicates, and reports an
unavailable database as a typed result retaining the original cause. It extends the existing pure
email parser and demonstrates grouping pure feature DTOs in [`types.py`](types.py) alongside narrow
behavioral units in [`registration_log.py`](registration_log.py) and [`register_email.py`](register_email.py).

From the repository root, run these commands explicitly on your machine:

```sh
python src/python/verify.py
python -m pytest test/use_case/python
```

The unit command needs no Docker. Use-case tests that exercise disposable PostgreSQL containers require
a running Docker engine; missing Docker makes a requested container run nonzero and blocked, never a
silent skip or fallback to mocks.

Construct `RegistrationLog` with a deployment version and a synchronous host-owned sink callable, then call
`register_email(store, logger, email, correlation_id, monotonic_now=...)`. Correlation IDs must
be opaque ASCII letters/digits/underscore/hyphen, at most 64 characters. Use a host-generated ID,
not a business identifier. The host owns secrets and logging destination configuration.

## Diagnostic recipe and limits

`RegistrationLog` emits JSON lines with timestamp, deployment version, operation, correlation ID,
dependency, attempt count, duration in milliseconds, severity and a stable code. The operation owns
one `registration.completed` record. Success and duplicate/validation rejection are Info; dependency
failure is Error; caller cancellation (`asyncio.CancelledError`) is Info and propagates the original
cancellation exception. Raw emails, credentials, exceptions, stacks and nested causes are never supplied
to the encoder.

`registration.slow` is Warn when database work exceeds a **provisional 250 ms** elapsed threshold.
It remains visible on successful operations. Inspect this signal alongside the terminal record by
correlation ID; compare counts/durations for equivalent workloads, time windows and deployment
versions before and after a fix. A healthy controlled-clock test emits no warning.

Sink exceptions increment `lost_records` independently and cannot change the business result. A host
must inspect that counter independently, choose a bounded sink and own monitoring/retention.

## Maintain public headers locally

```sh
python src/python/check.py skills/dev-standards/references/python/examples --update-headers
python src/python/check.py skills/dev-standards/references/python/examples
```

The updater reuses the checker's declaration inventory and only replaces a recognized initial ASCII
tree. It preserves authored responsibility text, API documentation and newline style, and is
idempotent.
