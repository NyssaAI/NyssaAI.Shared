# PostgreSQL registration example

> [!IMPORTANT] Scope Directive
> Persistence and database integration reference examples for TYPESCRIPT in dev-standards.

This small workflow registers one normalized email address, rejects duplicates, and reports an
unavailable database as a typed result retaining the original cause. It extends the existing pure
email parser. There is one public operation rather than an application/repository abstraction stack:
this sample has one provider and no independent business policy requiring another layer.

From the TypeScript package directory, run these commands explicitly on your machine:

```sh
npm ci --ignore-scripts
npm run build
npm run test:unit
npm run test:usecase
```

The unit command needs no Docker. The use-case command needs a running Docker engine and access to
the pinned PostgreSQL image on its first run; it fails when the dependency is unavailable. `npm test`
runs the unit suite. Select one workflow with `npx vitest run registerEmail.usecase.test.ts -t newAddress`;
the selected test acquires its own prerequisites. No CI configuration is supplied or required.

Read `registerEmail.usecase.test.ts` for the public setup, operation and expected result. Construct
`RegistrationLog` with a deployment version and a synchronous host-owned sink, then call
`registerEmail(connectionString, logger, email, correlationId, abortSignal)`. Correlation IDs must
be opaque ASCII letters/digits/underscore/hyphen, at most 64 characters. Use a host-generated ID,
not a business identifier. The host owns secrets and logging destination configuration.

`startPostgresFixture` owns unique names, generated credentials, localhost dynamic ports, bounded
readiness, the production `001_registrations.sql` migration and cleanup. It registers cleanup before
fallible startup work and removes both the container and its anonymous volume after failure or
completion. A deliberately invalid migration test verifies partial-startup cleanup. Each persistence
assertion opens a fresh observer connection after the operation's connection has closed. Tests prove
committed database state across connection lifetimes, not crash recovery or persistence after container
removal. Database failure is demonstrated by stopping the real dependency.

## Diagnostic recipe and limits

`RegistrationLog` emits JSON lines with timestamp, deployment version, operation, correlation ID,
dependency, attempt count, duration in milliseconds, severity and a stable code. The operation owns
one `registration.completed` record. Success and duplicate/validation rejection are Info; dependency
failure is Error; caller cancellation is Info and still rejects with the original abort reason.
Raw emails, credentials, exceptions, stacks and nested causes are never supplied to the encoder.

`registration.slow` is Warn when database work exceeds a **provisional 250 ms** elapsed threshold.
It remains visible on successful operations. Inspect this signal alongside the terminal record by
correlation ID; compare counts/durations for equivalent workloads, time windows and deployment
versions before and after a fix. A healthy controlled-clock test emits no warning. No automatic retry
or fallback is implemented, so there are no retry signals or retry policies to configure.

The sink is synchronous and must return promptly; this example does not support asynchronous network
exporters or contain a malicious/blocking callback. There is no queue, sampling or flush lifecycle.
Sink exceptions increment `lostRecords` independently and cannot change the business result. A host
must inspect that counter independently, choose a bounded sink and own monitoring/retention. Tests
capture actual serialized lines and prove thrown-sink isolation, redaction, correlation, failure and
controlled latency evidence. They do not establish a remote collector, alert delivery, deployed
monitoring, hard real-time sink budgets or production observability conformance.

Connections and queries have 1500 ms limits. Abort closes the owned connection and rejects; a write
may already have committed when cancellation or transport failure is observed. Read current state
before retrying. This is a small connection-per-operation example, not a connection pooling tutorial.

## Maintain public headers locally

```sh
node scripts/check.mjs examples --update-headers
node scripts/check.mjs examples
```

The updater reuses the checker's declaration inventory and only replaces a recognized initial ASCII
tree. It preserves authored responsibility text, API documentation and newline style, and is
idempotent. It leaves unsupported or malformed source and missing/ambiguous headers unchanged;
review those diagnostics manually. It does not invent a responsibility statement or excuse unrelated
rule violations. Review the resulting diff before keeping it.

Driver and fixture details follow the [node-postgres client API](https://node-postgres.com/apis/client)
and [Docker container CLI](https://docs.docker.com/reference/cli/docker/container/run/).
