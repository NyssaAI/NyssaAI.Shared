# C# code smells and anti-patterns

> [!IMPORTANT] Scope Directive
> Catalog of code smells and anti-patterns to assist code review under CS-REVIEW and CS-TEST. It defines observable signals, concrete architectural consequences, and minimal corrections; it does NOT authorize automatic refactoring or cosmetic churn based on smell labels alone.

Review companion to **CS-REVIEW**, **CS-TEST** and the [language rules](../../rules/csharp-standards.md).
Read entries relevant to the change; this catalog does not start another audit or workflow.

## How to judge and report

- **Violation:** evidence demonstrates a breach of an applicable explicit rule. Correct it or
  record a scoped, authorized exception.
- **Smell:** investigate before recommending a change. Length, duplication, an interface or a
  helper alone is not a defect. Establish the underlying problem and consequence.
- Each finding needs a source location, observed evidence, applicable rule or demonstrated
  problem, consequence, smallest correction and focused verification. Label unexecuted behavior
  as unverified; a source review does not establish test results.
- Prioritize broken behavior, misleading test evidence, leaks and unsafe logging. Do not invent
  findings to reach a quota or count the same underlying defect more than once.
- Preserve justified patterns and simple code. Resolve the consuming repository's applicable
  rules and revision; stale cached guidance cannot replace an explicitly supplied newer rulebook.

The distinction between a smell and a demonstrated problem follows
[Martin Fowler's explanation](https://martinfowler.com/bliki/CodeSmell.html). The mandatory
policies here are project choices, not universal language requirements.

Examples below are schematic review fragments, not complete programs or verified library APIs.
Use the actual subject's public API; these fragments are not approved runnable samples.

## Production code

### DESIGN-01 — Mixed responsibilities

**Smell; violation when CS-BOUNDARY or SOLID-S is demonstrably broken.**
Bad example: one endpoint owns eligibility rules, response formatting and database queries.
Unrelated changes now affect the same component. Keep policy with its feature, transport at
delivery and external I/O in an adapter; split at real responsibility/change boundaries.
**Counterexample:** a short orchestrator may call those collaborators. Immutable data-only types
are supported by CS-DATA; do not attach I/O to them to remove a supposed data-class smell.

### DESIGN-02 — Speculative abstraction

**Smell; assess SOLID-O, SOLID-I and CS-TEST.**
Bad example: `BaseScenario<T>` -> `ScenarioBuilder` -> `ScenarioRunner` for two direct tests,
or a factory/strategy for a single fixed calculation. The indirection obscures work and increases
maintenance. Prefer direct calls and ordinary assertions; extract demonstrated variation or
repeated resource management only.
**Counterexample:** native fixtures, actual interchangeable providers, a narrow I/O contract and
shared behavioral contract tests can earn their cost. Neither an interface with one current
implementation nor a little repeated setup is automatically wrong.

### DESIGN-03 — Hidden dependencies

**Smell; assess SOLID-D, CS-BOUNDARY and CS-CONSTRUCTION.**
Bad example: `ServiceLocator.Get<IProvider>()` inside business logic. Results depend on ambient
configuration and lifetime ownership. Accept the provider through the constructor and make the
composition root's ownership explicit.
**Counterexample:** an immutable constant or a deliberately shared, synchronized resource owned
by the application host is not an unowned mutable global. Identify the actual coupling defect.

### DESIGN-04 — Fabricated success and lost failures

**Violation of CS-FAILURE and potentially SOLID-L.**
Bad example: `catch (Exception) { return Result.Success(); }`. Callers receive success despite
failure. Translate recoverable adapter faults into canonical Nyssa Results, retain internal
causes and honor CancellationToken; fatal conditions are not ordinary business failures.
**Counterexample:** a documented fallback may succeed when it actually delivers its contract
and preserves required degradation evidence. Not every error-handling branch swallows failure.

### DESIGN-05 — Unusable or unsafe operational logs

**Violation of CS-LOG at an owned operational boundary.**
Bad examples: `logger.LogError(connectionString)`, discarded failures, duplicate terminal records
or invisible recovered retries. These expose sensitive data or obstruct diagnosis. Use configured
ILogger templates/scopes, allowlisted fields, correlation and safe cause codes. Name the terminal
owner and test actual emitted records, including sink-failure behavior.
**Counterexample:** pure parsers/values need no logger; distinct nested operations can have
distinct correlated outcomes. Follow the [logging contract](logging.md), not a demand to log every method.

### DESIGN-06 — Active Record, in-place domain mutation, and leaked persistence sessions

**Violation of CS-DOMAIN, CS-PERSISTENCE, and CS-BOUNDARY.**
Bad examples: `await order.SaveAsync()`, mutating `order.Status = Cancelled` in-place on a tracked entity, returning `IQueryable<Order>` or `DbContext` to application/delivery layers, or relying on `Database.EnsureCreated()`. These couple domain policy to database sessions, hide SQL side effects behind property access, and break AOT/test isolation. Model domain state as immutable expanded-body `record` types with pure transition methods returning `Result<T>` (`with { ... }`), keep `DbContext`/`DbDataReader` strictly inside `Infrastructure` repositories, and run versioned SQL migrations.
**Counterexample:** trivial pass-through CRUD handlers with no domain invariants may construct and persist immutable DTO records directly through a narrow repository interface without an intermediate domain transition method.

### DESIGN-07 — Reflection-driven dispatch, auto-scanning DI, and AOT-hostile mapping

**Violation of CS-PORTABILITY, CS-WORKFLOW, and CS-PERSISTENCE.**
Bad examples: `services.Scan(...)`, `AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...))`, `AutoMapper` runtime reflection profiles, or unannotated `JsonSerializer.Deserialize<T>()` in Native AOT targets. These fail under trimming/AOT or defer wiring failures to runtime. Register services and command/query handlers explicitly at the composition root (or via compile-time source generators) and map records explicitly or with compile-time source generators (`Mapperly`, `JsonSerializerContext`).
**Counterexample:** direct constructor-injected handler/service calls for simple components or an explicitly registered, static generic Command/Query Bus with pipeline middleware are AOT-safe and valid.

### DESIGN-08 — Unlogged or ambient-wired action handlers

**Violation of CS-WORKFLOW, CS-LOG, and CS-CONSTRUCTION.**
Bad examples: a Command/Query Handler, Application Service, or Repository that instantiates dependencies with `new`, reads a static logger/service locator, or executes workflows without an injected `ILogger`. Every action-taking class must receive its collaborators and structured `ILogger` through constructor Dependency Injection, emitting `Info`/`Warn`/`Error` at owned operational boundaries and `Debug`-level step telemetry (routed to diagnostic sinks such as Sentry or PostHog when debug mode is enabled).
**Counterexample:** pure domain records, value objects, and DTOs remain dependency-free and logger-free; they return `Result<T>` so the owning handler or service can log the outcome.

## Use-case tests

TEST-01 through TEST-09 correspond to the nine supplied use-case review patterns. They are
generalized review guidance, not independent confirmation of those repository findings.

### TEST-01 — API-shape checks presented as use cases

**Violation of CS-TEST when used as executable-workflow evidence.**
Bad example: `Assert.NotNull(typeof(IRepository).GetMethod("ExecuteAsync"))` without invoking it.
Checking signatures, attributes, setters or generic types can pass while execution is broken.
Call the real public API, assert the returned behavior and observe its effects. Keep pure
key-equality/hash cases in the unit suite.
**Counterexample:** supplemental API-compatibility/metadata checks may live in a clearly named
separate suite; reflection checks cannot replace caller tutorials. Type checks complement runtime evidence.

### TEST-02 — Scenario names choose the workflow

**Violation of CS-TEST's executable-documentation/KISS requirement.**
Bad example: a Theory switches on `"inline"`, `"factory"`, `"stored"`, `"before"` or `"after"` to
select setup, actions or assertions. Readers must mentally execute branches to understand usage.
Write separate named tests for each registration route, ordering case, duplicate registration
and invalid declaration. Use Theory to vary data for the same behavior.
**Counterexample:** descriptive case names are fine when they do not control execution. Running
the same behavioral contract against provider instances and ordinary setup/error guards are valid.

### TEST-03 — Configuration tutorials conceal public setup

**Violation of CS-TEST when registration/configuration is the subject.**
Bad example: `workstation.AddConfiguration(options)` secretly registers services, applies more
options, builds the host and opens a scope. It teaches a private test API and can bypass real
composition. Let the fixture supply resources/configuration values; visibly call the real public
registration overload, apply meaningful options, build the host and create/use its scope.
**Counterexample:** a ready client can suit another behavior test where registration is not the
subject. Infrastructure mechanics stay hidden; the public setup being taught stays visible.

### TEST-04 — Logging assertions never observe the claimed output

**Violation of CS-TEST and CS-LOG for a claimed sink/output behavior.**
Bad examples: checking `IsEnabled` and absence of a file as proof of stderr delivery, or checking
only a working destination as proof of a promised sink-failure diagnostic. Both can pass when
the claimed records are discarded. Emit a safe recognizable probe, flush/await bounded completion,
capture actual output and assert records and relevant fields/severity. For stderr-only output,
assert delivery there and absence from excluded destinations. Independently capture and assert
the promised failure diagnostic. Keep capture/stream restoration in fixtures and assertions in tests.
**Counterexample:** a filter/IsEnabled unit test is valid when that predicate is its stated subject.
A logger-call spy cannot establish delivery; exact human-readable wording need not be frozen.

### TEST-05 — Provisioning SQL lives in the test body

**Violation of CS-TEST's fixture boundary for ordinary caller tutorials.**
Bad example: `await fixture.ExecuteSetupAsync("CREATE TABLE task (...)")`. A helper wrapping SQL
execution still leaves provisioning owned by every test. Schema drift and setup details obscure
the workflow. Move schema creation/reset and production-migration execution into the fixture;
keep scenario values and public command construction in the test.
**Counterexample:** SQL that is itself the public command being demonstrated, or a deliberate
migration under test, must stay visible. Scenario seed values stay visible even if the fixture
performs the mechanical insert. Moving every SQL string indiscriminately would hide the subject.

### TEST-06 — Connectivity mistaken for complete provider integration

**Violation of CS-TEST when claimed as a complete custom-provider use case.**
Bad example: register a provider, resolve `IRepositoryProvider`, execute raw `SELECT 1` and stop.
This proves registration/connectivity but can pass with a broken repository command pipeline.
Register the custom provider and application command through public setup, resolve `IRepository`,
execute the command through that interface and assert the returned outcome and observable effects.
Mocking the pipeline being demonstrated does not close this gap.
**Counterexample:** keep a separate connectivity smoke test if useful and label its limited scope.
Shared contract tests should exercise every required behavior against each accepted provider.

### TEST-07 — Persistence evidence reuses the setup connection

**Violation of CS-TEST's fresh-observer requirement when claiming persistence.**
Bad example: `fixture.SetupConnection.Read(...)` through a long-lived in-memory database connection.
An observer independent of the writer provides useful evidence, but is not a freshly opened observer.
Complete/commit the write, open a new observer connection or an independent consumer that acquires
one, and assert the stored values. Fixtures may own connection mechanics; assertions stay visible.
For durable-storage claims use persistent backing, such as file-backed SQLite when SQLite is the
target, or the actual server engine. Reopen across the claimed lifetime/restart boundary without
dropping data. A fresh read alone does not establish crash durability or physical-disk guarantees.
**Counterexample:** shared in-memory storage is useful for explicitly bounded tests with its
lifetime stated. It does not prove durable storage or replace coverage of the actual server engine.
Fresh observation does not require a new container per assertion.

### TEST-08 — Cleanup begins after fallible setup

**Violation of CS-TEST and resource ownership.**
Bad example: create a temporary directory, write a file, then enter `try/finally`. A failed write
leaks the directory. Acquire through a disposable fixture and establish cleanup immediately,
before the next fallible operation. The fixture's constructor/start method must clean partial
resources itself if it fails before returning; later DisposeAsync alone is insufficient.
Keep scenario-specific file contents visible, for example a test writing to `fixture.FilePath`
inside the lifetime of `using var fixture = new TemporaryDirectoryFixture()`.
**Counterexample:** native automatic ownership or a correctly placed try/finally can be sufficient.
Avoid adding a base class just for disposal. Dispose scopes/hosts before owned dependencies and
never dispose a borrowed provider. The same partial-startup rule covers containers and networks.

### TEST-09 — External provisioning couples execution layers

**Violation of CS-TEST's independent execution/lifecycle requirement.**
Bad example: `run-tests.ps1` starts PostgreSQL before a combined unit/use-case run; tests only
accept an external connection. Unit tests now need Docker, and a selected tutorial cannot set up
its own prerequisites. Provide separate unit and use-case commands. Use-case fixtures own the
database startup, readiness, migrations, isolation and cleanup, including for directly selected tests.
The developer provides a Docker-capable host and runs the applicable local suites. Missing Docker
must make a requested integration run nonzero and blocked, never silently skip or fall back to mocks.
**Counterexample:** an all-tests convenience command may invoke the separate suites. The local environment can
prepare images/the daemon; a documented optional existing-service mode is allowed without making
the default unit command depend on it or replacing the fixture-owned default use-case path.

### TEST-10 — Order-dependent or timing-dependent fixtures

**Smell; a demonstrated isolation/readiness breach violates CS-TEST.**
Bad examples: test B needs data left by A, fixed ports collide, or fixed sleeps substitute for
readiness. Failures depend on order, load or concurrency. Isolate/reset scenario data, allocate
dynamic ports and use bounded condition-based waits. Run an affected test alone and vary order
or parallelism where supported to check the suspected dependency.
**Counterexample:** a shared expensive container is valid with isolated data and lifetimes.
A deliberate clock/timing test can control time as its actual subject; data-driven cases do not
have to execute concurrently.

## Evidence and limits

These patterns guide review; the structural checker does not prove them. Confirm findings
against the actual implementation and claimed scope. For each behavioral correction, identify
a plausible broken implementation that its assertion should reject. A discarded log write,
omitted command execution or missing persistence write should not pass a test claiming that behavior.

Record supplied review provenance separately. Do not claim schematic examples, authored evaluation
prompts or source-only reviews have executed. The [testing contract](testing.md) remains the authority
for test layers, fixture boundaries and local execution evidence.
