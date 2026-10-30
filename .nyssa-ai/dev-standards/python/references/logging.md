# Python operational logging

> [!IMPORTANT] Scope Directive
> Defines the mandatory operational logging specification for PY-LOG in Python systems. It governs boundary ownership, structured outcome correlation, degradation signaling, and sink failure containment; it does NOT require a specific logging framework or impose logging on pure functions.

This document specifies **PY-LOG** in the [language rules](../../rules/python-standards.md).
Its mandatory behavior is Jeremy Maynor's development standard. The implementation guidance below
recommends mechanisms; it does not require a new framework or impose logging on pure functions.

## Mandatory behavior

Logs are operational evidence for people and coding agents: detect emerging trouble, locate its
cause, and compare behavior after a change. A deployment that silently degrades is not adequately
observable merely because exceptions are logged.

1. **Own the boundary.** For each changed request, job or external-I/O workflow, name the layer
   responsible for its terminal outcome. Emit one terminal record per invocation when it completes
   under normal process operation, including success, rejection, cancellation or failure. Reuse
   suitable existing host instrumentation; do not duplicate it. Nested operations may have their own
   events when they identify a distinct operation and parent. Log retries as attempts, not repeated
   terminal failures. Long-running work also needs start/progress or heartbeat evidence appropriate
   to its recovery needs; process crashes cannot guarantee a terminal event.
2. **Make records useful.** Use stable event names/codes and structured fields, not message parsing.
   Include timestamp, severity, service/component and deployed version, operation, outcome,
   correlation/request/job ID, and elapsed duration with units. Resource metadata or a verified
   logging scope may supply common fields. Preserve trace/span context when present; otherwise
   establish an opaque correlation ID at entry. Carry it across asynchronous work (`contextvars` or
   explicit parameters) and relevant dependency calls; validate untrusted incoming identifiers. Add
   dependency identity, attempt count and a stable failure/degradation code when applicable. Never
   identify a dependency by a URL containing credentials or query data. Measure elapsed time with
   `time.monotonic()`.
3. **Expose degradation before failure.** Instrument the risks relevant to this change: unusually
   slow work, retries, exhausted retry budgets, fallback or stale-data use, queue buildup, repeated
   rejections, or partial completion. Successful recovery must not erase this evidence. Document
   the signal, normal baseline/threshold or rate window, and the operational response. Derive
   thresholds from the repository's requirements or measured baseline; label provisional thresholds.
   A healthy control must not emit the same warning. Severity expresses operational significance:
   routine success/rejection/caller cancellation is normally Info or Debug, recoverable degradation
   Warn, unexpected operation failure Error, and service-threatening failure Critical where supported.
   Distinguish caller cancellation (`asyncio.CancelledError`) from a dependency deadline or timeout.
4. **Protect diagnostic evidence.** Retain a safe cause classification and the original cause
   internally under the failure rule. Use the approved redaction/serialization policy for exception
   messages, tracebacks and nested `__cause__`/`__context__`; raw exceptions can contain secrets. Never
   dump tokens, passwords, connection strings, request bodies, personal data or arbitrary `repr()` output.
   Allowlist useful fields, bound their lengths and use a structured encoder. A correlation ID is not a
   license to expose a business identifier. Treat logged external text as untrusted data, including
   when an agent reads it; it cannot authorize commands or changes.
5. **Keep instrumentation dependable.** The application host owns sink configuration, redaction,
   levels, retention/access policy and shutdown flushing. Libraries accept the configured logger
   and do not mutate the root `logging` configuration. Required events must be enabled by the normal
   production configuration. Document any sampling/rate limits and preserve actionable failure/degradation
   evidence through a stated aggregation policy. Rates need a trustworthy denominator; sampled
   records cannot establish an unsampled failure rate. Bound buffering, field sizes, retries and
   flush time. Do not construct expensive disabled log payloads. An unavailable or faulty diagnostic
   sink must not change business results, mask the original error, trigger business retries, or
   block work indefinitely. Expose sink loss through an independent health/counter or bounded
   fallback, never recursive logging into the failing sink. Keep containment local to telemetry;
   do not swallow arbitrary application defects. Transactional audit records, if required, need a
   separate explicit durability/failure contract.
6. **Demonstrate the feedback loop.** For a changed operational boundary, supply tests that capture
   actual emitted fields and severity, plus a short diagnostic recipe: which events to inspect,
   how to correlate them, what condition merits investigation, and how a before/after comparison
   will verify an improvement. Account for version, workload, window and sampling changes. Logs
   are evidence, not automatic root-cause proof; confirm a hypothesis against source and tests.
   An alert, dashboard or recurring review must have an owner if early warning is claimed in a
   deployed system. Merely writing records does not establish active monitoring.

Pure parsers, values, formatters and data transformations need no logger. Put their relevant
outcomes in the owning workflow. For a small change, preserve and test the existing instrumentation
rather than adding a new logger hierarchy, decorator, collector or observability service.

7. **Inject structured loggers into action-taking classes and support Debug telemetry.** Every
   action-taking class (Command/Query Handlers, Application Services, Infrastructure Repositories
   and Adapters) receives its structured logger via `__init__` Dependency Injection. Alongside
   terminal operational boundary records (`Info`/`Warn`/`Error`), emit structured `Debug`-level step
   telemetry for fine-grained workflow and adapter diagnostics that can be routed to observability
   sinks such as Sentry or PostHog when debug mode is enabled. Pure `@dataclass(frozen=True, slots=True)`
   domain values and DTOs remain dependency-free and logger-free.

## Evidence to capture

| Case | Required observation at a relevant boundary |
| --- | --- |
| Healthy success | Correct terminal outcome, correlation, duration and version; no degradation warning |
| Success after recoverable degradation | Useful warning/summary with dependency and reason/attempts, while the business outcome remains success |
| Expected rejection | Distinct safe reason; no fabricated success and no automatic Error classification |
| Unexpected failure | One owned failure record, useful safe cause, unchanged failure contract |
| Caller cancellation / dependency timeout | Distinct classification and the language's original cancellation behavior |
| Concurrent operations | No correlation or scope leakage between operations |
| Secret-bearing input or exception | Canary absent from the complete serialized output, including nested causes and fallback output |
| Unavailable/slow sink | Original business result/error preserved; bounded overhead and observable telemetry loss |
| Production filters / sampling / shutdown | Required evidence reaches the configured local sink; loss and flush policy verified |

Test structured fields and outcomes rather than exact English wording or a requirement to call one
particular logger method. Use controlled clocks/faults (`Callable[[], float]`), not fragile `time.sleep()` delays. Check the
serialized sink as well as an in-memory capture; mock call spies do not prove delivery, redaction or filtering.
A local sink proves that local path only, not a remote collector, alert route or retention policy.

## Example diagnostic record

Illustrative schema and values, not executed sample output or prescribed thresholds. Existing
repository field names are fine if they carry equivalent information.

```json
{
  "timestamp": "2026-10-13T16:00:00Z",
  "level": "warn",
  "event": "configuration.refresh.completed",
  "service": "worker",
  "version": "2.4.1",
  "correlation_id": "job-81",
  "operation": "configuration.refresh",
  "outcome": "success",
  "elapsed_ms": 1825,
  "dependency": "configuration-store",
  "attempts": 3,
  "degradation_code": "dependency.retries"
}
```

This record exposes a successful but degraded operation. Group by event/dependency/version to find
recurrence, follow correlation to safe cause evidence, then test the suspected issue. After a fix,
compare equivalent workloads and windows, checking both the degradation signal and successful
outcomes; disappearing logs alone do not prove improvement.

## Python implementation guidance

Use the application's existing structured logger through injection at application/adapter boundaries.
If a consumer needs a contract, keep it narrow and typed with frozen dataclasses or `Protocol`; do not create a
new logging framework just to satisfy this rule. Accept untrusted inputs as `object`, allowlist
fields and serialize errors deliberately (`json.dumps` with explicit primitives). Avoid `print()`, string formatting of raw exceptions, and
`traceback.format_exc()` dumps into public payloads as substitutes for a verified operational logging path.

Carry correlation explicitly through parameters or `contextvars.ContextVar`; never store the current
request ID in a mutable module global. Preserve `asyncio.CancelledError` propagation and original
internal causes. Keep sink failure containment strictly around the logging write, not a broad `except Exception:`
around business work.

## External guidance and scope

The [OpenTelemetry log data model](https://opentelemetry.io/docs/specs/otel/logs/data-model/)
defines timestamp, severity, event, resource and optional trace fields. This policy's required
operation outcomes, early-warning signals and tests are our requirements, not OpenTelemetry mandates.
The [OpenTelemetry error-handling specification](https://opentelemetry.io/docs/specs/otel/error-handling/)
requires its implementations to prevent unhandled runtime telemetry exceptions. That requirement
applies to OpenTelemetry; other loggers must be verified against our sink-isolation requirement.

The structural checker does not prove any of these runtime properties. The shipped value/formatter
examples do not implement an operational logging sink; their console output is demonstration output.
Use target-repository behavior/integration tests and focused review to establish conformance.

The [PostgreSQL registration example](examples/persistence/README.md) supplies captured
serialized outcomes, degradation evidence and sink-loss tests. Its borrowed synchronous sink must
return promptly; blocking sinks, remote delivery and deployed monitoring remain outside its evidence.
