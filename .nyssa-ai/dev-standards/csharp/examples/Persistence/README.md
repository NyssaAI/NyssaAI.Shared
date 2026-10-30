# Register an email in PostgreSQL

> [!IMPORTANT] Scope Directive
> Persistence and database integration reference examples for CSHARP in dev-standards.

This small library exposes `EmailRegistration.RegisterAsync` with canonical Nyssa `Result` outcomes.
It writes a normalized email through Npgsql, rejects duplicates, and opens and disposes a separate
unpooled connection for each operation. The existing pure email parser remains quiet. There is no
repository abstraction: one database operation does not yet justify interchangeable implementations.

## Run locally

Prerequisites: .NET 10, Python 3.12+, an authorized Nyssa.Shared checkout containing the revision in
[`dependency-lock.json`](../../../../../../src/csharp/dependency-lock.json), and Docker Desktop/Engine for use-case tests.
Run from this package directory:

```powershell
$env:NYSSA_SHARED_ROOT = 'D:/repos/jamaynor/Nyssa.Shared'
python scripts/verify_persistence.py
python scripts/verify_persistence.py --use-case
```

The first command runs four unit cases without Docker or a listening database. The second runs four
use-case scenarios against disposable PostgreSQL containers. Missing/unavailable Docker fails the
requested use-case run; there are no skips or substitute providers. Neither command configures CI.
The script archives the pinned canonical Result dependency into this package's ignored `.temp`
directory, without building or changing the supplied checkout.

For a directly selected xUnit scenario, pass the absolute pinned Result project path (created by
either command) and the runner's normal filter:

```powershell
dotnet run --project examples/Persistence/UseCaseTests/UseCaseTests.csproj "-p:NyssaResultProject=$PWD/.temp/nyssa/b7ecdfb0a5a9c4c0f51fc74546b1a4756703aa9b/Nyssa.Result/src/Nyssa.Result.csproj" -- -method "*StoppedDatabase*"
```

The xUnit fixture still provisions all prerequisites for a directly selected test. It owns the
digest-pinned PostgreSQL container, generated credentials, dynamic loopback port, 90-second readiness
deadline, real [migration](Registration/001_registrations.sql), and cleanup. Startup failures also
dispose the partially started container. Each class gets its own container; scenario email values
are disjoint, so stopping the failure class's database cannot affect the healthy class.

## Read the public workflow

[`EmailRegistrationTests`](../../../../../../test/use_case/csharp/Persistence/UseCaseTests/EmailRegistrationTests.cs) shows visible public construction,
registration, duplicate rejection, and a fresh connection querying persisted state. Other cases
verify successful writes survive a throwing diagnostic sink and a controlled monotonic duration
produces a warning without a test sleep. [`DatabaseFailureTests`](../../../../../../test/use_case/csharp/Persistence/UseCaseTests/DatabaseFailureTests.cs)
stops a real database and asserts canonical `Unavailable` failure plus the actual JSON failure record.
These are registration-use-case tests, not just container startup or `SELECT 1` checks.

The workflow checks caller cancellation before I/O and passes the token to Npgsql. A canceled call
returns canonical `Canceled`, distinct from a dependency timeout. Once a write has started,
cancellation may race with commit: read authoritative state before retrying. A transport failure
after sending the write returns `Indeterminate`; an unavailable connection returns `Unavailable`.
The Result retains original exception details for trusted handling; expose only `UserMessage` to
external callers. Do not serialize the complete Result or its technical fields into diagnostics.
No retry is attempted. Connections and commands have two-second deadlines.

## Diagnostic evidence and its limits

`EmailRegistration` owns one `registration.completed` outcome per invocation. The injected
`RegistrationLog` serializes only fixed fields, a typed opaque correlation ID, deployment version,
timestamp, monotonic `elapsed_ms`, attempt count and safe cause code. The correlation ID is also sent
as PostgreSQL's application name. Email, credentials and raw exception chains never enter the sink.
The host supplies a synchronous **promptly returning, nonblocking** sink and owns filters, retention,
access and flushing. This example has no global logging configuration, sampling, background worker
or retry queue. Records remain bounded by the schema and validated version label.

Sink exceptions increment the independent `LostRecords` counter and leave the business Result
unchanged; inspect that counter through a separate host health path. Fatal out-of-memory conditions
are not swallowed. An arbitrary blocking host callback cannot be interrupted by this wrapper: this
sample does not establish slow-sink isolation or a production telemetry delivery guarantee. Reuse
the application's bounded logging provider when integrating it into a real service.

The `registration.slow` warning uses a **provisional 250 ms** dependency threshold, not a measured
service-level objective. Healthy controlled-time tests emit no warning; a recovered 500 ms success
does. To diagnose: group warning/failure records by dependency and version, follow `correlation_id`,
then check database availability/latency and source evidence. After a fix, compare the same workload,
version, observation window and unsampled denominator; confirm business success as well as warning
rate. The host must assign an alert/review owner before claiming active early detection.

Captured JSON tests establish the local sink only. They do not establish remote collection,
production filter/flush policy, repeated-rejection alerting, restart durability, crash behavior,
concurrent correlation, or every in-flight cancellation/timeout race. Storage observation is across
fresh connections within the container lifetime, with no claim of survival after container deletion.

The implementation uses the [official Npgsql API](https://www.npgsql.org/doc/basic-usage.html)
and [Testcontainers PostgreSQL fixture](https://dotnet.testcontainers.org/modules/postgres/).
Version pins are in the two project files. Tests use native xUnit lifecycle and assertions;
there is no common cross-language test framework.
