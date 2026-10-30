# Python code smells and anti-patterns

> [!IMPORTANT] Scope Directive
> Catalog of code smells and anti-patterns to assist code review under PY-REVIEW and PY-TEST. It defines observable signals, concrete architectural consequences, and minimal corrections; it does NOT authorize automatic refactoring or cosmetic churn based on smell labels alone.

Review companion to **PY-REVIEW**, **PY-TEST** and the [language rules](../../rules/python-standards.md).
Read entries relevant to the change; this catalog does not start another audit or workflow.

## How to judge and report

- **Violation:** evidence demonstrates a breach of an applicable explicit rule. Correct it or
  record a scoped, authorized exception.
- **Smell:** investigate before recommending a change. Length, duplication, a protocol or a
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

**Smell; violation when PY-BOUNDARY or SOLID-S is demonstrably broken.**
Bad example: one FastAPI/Flask handler owns eligibility rules, response formatting and SQL queries.
Unrelated changes now affect the same function or class. Keep policy with its feature, transport at
delivery and external I/O in an adapter; split at real responsibility/change boundaries.
**Counterexample:** a short orchestrator may call those collaborators. Immutable frozen dataclasses
and `types.py` DTOs are supported by PY-DATA and PY-FILE; do not attach I/O to them to remove a supposed data-class smell.

### DESIGN-02 — Speculative abstraction

**Smell; assess SOLID-O, SOLID-I and PY-TEST.**
Bad example: `BaseScenario[T]` -> `ScenarioBuilder` -> `ScenarioRunner` for two direct tests,
or a factory/strategy class hierarchy for a single fixed calculation. The indirection obscures work and increases
maintenance. Prefer direct function calls and ordinary assertions; extract demonstrated variation or
repeated resource management only.
**Counterexample:** context-manager fixtures, actual interchangeable providers, a narrow I/O `Protocol` and
shared behavioral contract tests can earn their cost. Neither a `Protocol` with one current
implementation nor a little repeated setup is automatically wrong.

### DESIGN-03 — Hidden dependencies and mutable module state

**Smell; assess SOLID-D, PY-BOUNDARY and PY-LIFETIME.**
Bad example: reading or mutating a module-level singleton dict/client inside business logic, or using mutable default arguments (`def run(items: list[str] = [])`). Results depend on import order, ambient
configuration and shared state. Accept collaborators through parameters or constructors and make the
composition root's ownership explicit.
**Counterexample:** an immutable `Final` constant or a deliberately shared, thread-safe resource owned
by the application host is not an unowned mutable global. Identify the actual coupling defect.

### DESIGN-04 — Swallowed exceptions and fabricated success

**Violation of PY-FAILURE and potentially SOLID-L.**
Bad example: `except Exception: return Ok(None)` or catching `BaseException` and suppressing `asyncio.CancelledError`. Callers receive success despite
failure or lose cancellation. Translate expected dependency failures into `Err(...)` while retaining internal
causes, and allow cancellation (`CancelledError`, `KeyboardInterrupt`, `SystemExit`) to propagate.
**Counterexample:** a documented fallback may succeed when it actually delivers its contract
and preserves required degradation evidence. Not every error-handling branch swallows failure.

### DESIGN-05 — Unusable or unsafe operational logs

**Violation of PY-LOG at an owned operational boundary.**
Bad examples: `logger.error(f"Failed: {dsn} {payload}")`, discarded failures, duplicate terminal records
or invisible recovered retries. These expose sensitive data or obstruct diagnosis. Use the configured
structured logger with allowlisted fields, correlation and safe cause codes. Name the terminal
owner and test actual emitted records, including sink-failure behavior.
**Counterexample:** pure parsers/values need no logger; distinct nested operations can have
distinct correlated outcomes. Follow the [logging contract](logging.md), not a demand to log every function.

### DESIGN-06 — Active Record, in-place domain mutation, and leaked ORM sessions

**Violation of PY-DOMAIN, PY-PERSISTENCE, and PY-BOUNDARY.**
Bad examples: `await order.save()`, mutating attributes in-place on a tracked ORM model, returning an active SQLAlchemy `AsyncSession` / `Session` or database cursor to `application` or `api` callers, or calling `Base.metadata.create_all()` in production startup. These couple domain rules to database state, trigger hidden lazy-load I/O on attribute access, and bypass deterministic migrations. Model domain state as `@dataclass(frozen=True, slots=True)` records with pure transition functions/methods returning `Result[T, E]` (`dataclasses.replace(self, ...)`), keep database sessions strictly inside `infrastructure` adapters, and run versioned SQL migrations.
**Counterexample:** trivial pass-through CRUD handlers with no domain invariants may construct and persist frozen DTO dataclasses directly through a narrow repository `Protocol` without an intermediate domain transition method.

### DESIGN-07 — Dynamic module scanning, monkey-patching, and OS-coupled core modules

**Violation of PY-PORTABILITY, PY-WORKFLOW, and PY-PERSISTENCE.**
Bad examples: `importlib` auto-scanning for command handlers in business packages, monkey-patching collaborators at runtime, or hardcoding POSIX/Windows file-system and process assumptions inside `domain` or `application` modules. These obscure dependency graphs, break static type checking (`mypy --strict`), and fail across platforms. Wire dependencies and Command/Query Bus handlers explicitly at the composition root and isolate OS-specific mechanics behind narrow `Protocol` ports in `infrastructure`.
**Counterexample:** direct constructor-injected handler/service calls for simple packages or an explicitly registered, type-safe Command/Query Bus with logging/telemetry middleware are valid.

### DESIGN-08 — Unlogged or ambient-wired action handlers

**Violation of PY-WORKFLOW, PY-LOG, and PY-LIFETIME.**
Bad examples: a Command/Query Handler, Application Service, or Repository class that calls `logging.getLogger()` directly inside methods without injection, instantiates its own database client, or executes workflows without an injected structured logger. Every action-taking class must receive its collaborators and structured logger through `__init__` Dependency Injection, emitting `Info`/`Warn`/`Error` at owned operational boundaries and `Debug`-level step telemetry (routed to diagnostic sinks such as Sentry or PostHog when debug mode is enabled).
**Counterexample:** pure `@dataclass(frozen=True, slots=True)` domain values, parsers, and `types.py` DTOs remain dependency-free and logger-free; they return `Result[T, E]` so the owning handler or service can log the outcome.

## Use-case tests

TEST-01 through TEST-09 correspond to the nine supplied use-case review patterns. They are
generalized from supplied C# observations. These are analogous failure modes, not claims
of findings in this language's repositories.

### TEST-01 — API-shape checks presented as use cases

**Violation of PY-TEST when used as executable-workflow evidence.**
Bad example: `assert callable(getattr(repository, "execute", None))` without invoking it.
Checking attributes, annotations or method signatures can pass while execution is broken.
Call the real public API, assert the returned behavior and observe its effects. Keep pure
key-equality/hash cases in the unit suite.
**Counterexample:** supplemental API-compatibility/metadata checks may live in a clearly named
separate suite; reflection checks cannot replace caller tutorials. Type checks complement runtime evidence.

### TEST-02 — Scenario names choose the workflow

**Violation of PY-TEST's executable-documentation/KISS requirement.**
Bad example: a `@pytest.mark.parametrize` test switches on `"inline"`, `"factory"`, `"stored"`, `"before"` or `"after"` to
select setup, actions or assertions. Readers must mentally execute branches to understand usage.
Write separate named tests for each registration route, ordering case, duplicate registration
and invalid declaration. Use parameterization to vary data for the same behavior.
**Counterexample:** descriptive case IDs are fine when they do not control execution. Running
the same behavioral contract against provider instances and ordinary setup/error guards are valid.

### TEST-03 — Configuration tutorials conceal public setup

**Violation of PY-TEST when registration/configuration is the subject.**
Bad example: `workstation.add_configuration(options)` secretly chooses providers, applies options
and constructs/starts the application. It teaches a private test API and can bypass real
composition. Let the fixture supply resources/configuration values; visibly call the real public
factory/registration function, apply meaningful options and show the application lifecycle.
**Counterexample:** a ready client can suit another behavior test where registration is not the
subject. Infrastructure mechanics stay hidden; the public setup being taught stays visible.

### TEST-04 — Logging assertions never observe the claimed output

**Violation of PY-TEST and PY-LOG for a claimed sink/output behavior.**
Bad examples: checking `logger.isEnabledFor(logging.INFO)` and absence of a file as proof of stderr delivery, or checking
only a working destination as proof of a promised sink-failure diagnostic. Both can pass when
the claimed records are discarded. Emit a safe recognizable probe, flush/await bounded completion,
capture actual output and assert records and relevant fields/severity. For stderr-only output,
assert delivery there and absence from excluded destinations. Independently capture and assert
the promised failure diagnostic. Keep capture/stream restoration in fixtures and assertions in tests.
**Counterexample:** a filter/`isEnabledFor` unit test is valid when that predicate is its stated subject.
A `unittest.mock.MagicMock` logger spy cannot establish delivery; exact human-readable wording need not be frozen.

### TEST-05 — Provisioning SQL lives in the test body

**Violation of PY-TEST's fixture boundary for ordinary caller tutorials.**
Bad example: `fixture.execute_setup("CREATE TABLE task (...)")`. A helper wrapping SQL
execution still leaves provisioning owned by every test. Schema drift and setup details obscure
the workflow. Move schema creation/reset and production-migration execution into the fixture;
keep scenario values and public command construction in the test.
**Counterexample:** SQL that is itself the public command being demonstrated, or a deliberate
migration under test, must stay visible. Scenario seed values stay visible even if the fixture
performs the mechanical insert. Moving every SQL string indiscriminately would hide the subject.

### TEST-06 — Connectivity mistaken for complete provider integration

**Violation of PY-TEST when claimed as a complete custom-provider use case.**
Bad example: register a provider, obtain the provider implementation, execute raw `SELECT 1` and stop.
This proves registration/connectivity but can pass with a broken repository command pipeline.
Register the custom provider and application command through public setup, construct the repository through its public API,
execute the command through that interface and assert the returned outcome and observable effects.
Mocking the pipeline being demonstrated does not close this gap.
**Counterexample:** keep a separate connectivity smoke test if useful and label its limited scope.
Shared contract tests should exercise every required behavior against each accepted provider.

### TEST-07 — Persistence evidence reuses the setup connection

**Violation of PY-TEST's fresh-observer requirement when claiming persistence.**
Bad example: `fixture.setup_connection.execute(...)` through an uncommitted or shared in-memory database connection.
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

**Violation of PY-TEST and resource ownership.**
Bad example: create a temporary directory or start a container, run a fallible command, and only register teardown afterwards. A failed setup step
leaks the resource. Acquire through a context manager or `contextlib.ExitStack` and register cleanup immediately,
before the next fallible operation. The fixture's `__enter__` / start method must clean partial
resources itself if it fails before returning.
**Counterexample:** `with tempfile.TemporaryDirectory()` or a properly structured `try ... finally` is sufficient.
Avoid adding a base class just for disposal. Close owned resources in reverse acquisition order and never close a borrowed provider.

### TEST-09 — External provisioning couples execution layers

**Violation of PY-TEST's independent execution/lifecycle requirement.**
Bad example: a wrapper script starts PostgreSQL before a combined unit/use-case pytest invocation; tests only
accept an external `DATABASE_URL`. Unit tests now need Docker, and a selected tutorial cannot set up
its own prerequisites. Provide separate unit and use-case commands. Use-case fixtures own the
database startup, readiness, migrations, isolation and cleanup, including for directly selected tests.
Missing Docker must make a requested integration run nonzero and blocked, never silently skip (`pytest.skip`) or fall back to mocks.
**Counterexample:** an all-tests convenience command may invoke the separate suites. The local environment can
prepare images/the daemon; a documented optional existing-service mode is allowed without making
the default unit command depend on it or replacing the fixture-owned default use-case path.

### TEST-10 — Order-dependent or timing-dependent fixtures

**Smell; a demonstrated isolation/readiness breach violates PY-TEST.**
Bad examples: test B needs rows left by A, fixed ports collide, or `time.sleep(2)` substitutes for
readiness. Failures depend on order, load or concurrency. Isolate/reset scenario data, allocate
dynamic ports and use bounded condition-based waits. Run an affected test alone and vary order
to check the suspected dependency.
**Counterexample:** a shared expensive container is valid with isolated data and lifetimes.
A deliberate clock/timing test can control time via an injected clock callable as its actual subject.

## Evidence and limits

These patterns guide review; the structural checker does not prove them. Confirm findings
against the actual implementation and claimed scope. For each behavioral correction, identify
a plausible broken implementation that its assertion should reject. A discarded log write,
omitted command execution or missing persistence write should not pass a test claiming that behavior.

Record supplied review provenance separately. Do not claim schematic examples, authored evaluation
prompts or source-only reviews have executed. The [testing contract](testing.md) remains the authority
for test layers, fixture boundaries and local execution evidence.
