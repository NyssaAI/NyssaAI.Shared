# TypeScript testing contract

> [!IMPORTANT] Scope Directive
> Defines the executable documentation and verification contract for TS-TEST in TypeScript and React codebases. It governs unit test isolation, public use-case integration tests, fixture-owned Docker lifecycles, and KISS testing practices; it does NOT prescribe rigid test counts or enforce testing of trivial boilerplates.

This document specifies **TS-TEST** in the [language rules](../../rules/typescript-standards.md).
These mandatory requirements express Jeremy Maynor's development style. Library mechanisms
linked below are implementation guidance, not claims that the libraries mandate this policy.

## Required test layers

- **Unit tests (`test/unit/`):** verify isolated behavior, invariants and meaningful edge/failure cases. Keep them
  fast and deterministic, without Docker, network services or shared persistent state. Use a
  small fake only at a real collaborator boundary; do not mock the implementation under test.
- **Use-case tests (`test/use_case/`):** exercise complete supported caller workflows through the real public
  interface, composition and adapters. For infrastructure-backed behavior, run the actual
  dependencies in disposable Docker containers. Use the supported engine, driver and production
  migrations; an in-memory replacement or mocked repository cannot establish database behavior.
  Assert externally observable results and side effects, important rejections and relevant
  dependency failures. Verify persisted state through a fresh connection/consumer when claiming
  persistence, so an in-process cache cannot satisfy the test. A long-lived setup connection
  is useful independent evidence but does not meet the fresh-observer requirement. Durable-storage
  claims also need persistent backing and observation across the claimed lifetime boundary.
- **End-to-end browser/system tests (`test/e2e/`, optional):** when a repository includes full-stack browser or UI system journeys (e.g., Playwright driving a running Next.js or web server), isolate those browser suites in `test/e2e/`. Do not use `test/e2e/` as a substitute for `test/use_case/` or co-mingle non-browser backend/database workflow tests into `test/unit/`.
- Cover changed behavior and critical supported workflows at the appropriate layers. Pure
  libraries still need complete caller examples, but need no artificial Docker dependency.
  Containerizing the test runner alone is not infrastructure integration evidence. Where an
  external service cannot run locally, identify the tested substitute and remaining contract
  evidence explicitly. Do not describe emulator coverage as proof of the hosted service.

## Executable documentation and KISS

Tests must teach correct, idiomatic use of the component and comply with the applicable language
rules. Use the **simplest coding style that clearly expresses the behavior**. Prefer explicit
local values, ordinary public calls and the test framework's standard assertions. Keep one
coherent scenario and a visible Arrange / Act / Assert sequence in each test; multiple assertions
are appropriate when they explain one outcome. Parameterize examples of the same behavior only.
Straightforward guards for setup failures and ordinary error handling remain appropriate.

Use established patterns when they solve a concrete need. Fixtures earn their cost by managing
resources; provider contract tests earn theirs by checking interchangeable implementations.
Avoid speculative abstractions, base-class hierarchies, generic builders and custom DSLs.
Caller tutorials must not rely on reflection, branching on scenario names or helper chains that
obscure public usage. Keep supplemental
API-shape/metadata checks in a separately named suite; they cannot replace executable caller
workflows. Pure key/equality/hash behavior belongs in the unit suite. Small duplication is
preferable to an abstraction that makes the intended usage harder to see. Do not minimize line
count at the expense of readable setup, error handling or assertions. A reader should understand
the behavior and public API usage without opening helper implementations.

## Fixture boundary

Fixtures own infrastructure mechanics: containers, generated credentials, dynamic ports,
connection details, production migrations, readiness checks, isolated data and cleanup. Keep
fixtures narrowly scoped and use the test framework's normal lifecycle. Provisioning SQL and
Docker operations belong in fixtures rather than in ordinary use-case test bodies. Passing
provisioning SQL to an execution helper does not move ownership out of the test. Keep scenario
values and any SQL/migration that is itself the subject visible.

Keep scenario inputs, business preconditions, meaningful public configuration, the operation under
test and business assertions visible in the test. A fixture may provide a ready dependency or
client; it must not secretly execute the workflow, replace production composition with mocks,
or assert the expected business result. A test about public registration/configuration must
show that setup explicitly, even when infrastructure provisioning is hidden.

Pin image versions (or digests), use dynamic ports and bounded readiness checks rather than fixed
sleeps, and guarantee cleanup on success, failure and partial startup. Register cleanup immediately
after acquisition, before another fallible action. A fixture constructor/start method must clean
its own partial resources if it fails before returning; later teardown alone cannot guarantee that.
Reusing a container within a suite is allowed when test data remains isolated and tests do not
depend on execution order.
Keep failures diagnosable with bounded application/container logs, safe configuration metadata
and correlation. Never print credentials or sensitive scenario data.

## Logging, execution and evidence

At owned operational boundaries, assert actual structured outcomes and relevant failure/degradation
records under the [logging contract](logging.md). A log call spy does not establish usable logs.
Capture the actual promised sink: enabled levels or absence of a file do not prove stderr output,
and a working fallback does not prove the promised failure diagnostic was emitted. Assert those
records after bounded flushing/collection. Keep business assertions in the tests and log
collection/redaction mechanics in fixtures.

Expose separate local unit and use-case commands. Run the applicable suites explicitly during
development and review; the fast unit command must work without Docker. Missing Docker makes
a requested use-case run blocked and nonzero, never a silent skip or fallback to mocks. This
standard does not require CI integration. Record
which suites actually ran. Each directly selected use-case test must acquire its prerequisites
through its fixture. A launcher may select suites or prepare the Docker host, but must not provision
a database before the unit-only command or replace fixture ownership with a required external
connection. Test source, compilation and a running container alone are not passes. Provider
registration plus SELECT 1 is a smoke test; complete provider coverage must execute an application
command through the consuming public interface and assert its result and effects.

Review test usefulness as well as green execution: a deliberately broken implementation should
fail a relevant assertion. For infrastructure examples, calibrate against omitted writes,
missing migrations and incorrect failure translation. Assess clarity and fixture boundaries by
review; structural checkers cannot prove KISS, meaningful coverage or complete correctness.

## TypeScript implementation guidance

Use Vitest lifecycle hooks or a small typed fixture with Testcontainers for Node.js. Await setup and cleanup; clean up partial startup too. Keep setup typed without any or unjustified non-null assertions. Prefer direct expect assertions and public calls over custom matchers, fluent builders or a test-specific workflow API.

The [official Testcontainers documentation](https://node.testcontainers.org/quickstart/usage/) describes the available lifecycle mechanisms.
Use the repository's supported versions and native test framework; adopting this standard does
not require a shared cross-language test harness.

## Review companion

Use the [code-smell catalog](code-smells.md#use-case-tests) for reflection-only coverage, branching
tutorials, hidden setup, unobserved logs, leaked provisioning, incomplete provider workflows,
persistence observers, partial setup failure and coupled execution. Its counterexamples prevent
blanket bans on legitimate contract tests, native fixtures and simple helpers.

## Current examples and limits

The [pure examples](examples/README.md) demonstrate public value/formatter usage without Docker.
The [PostgreSQL registration workflow](examples/persistence/README.md) adds runnable local use-case
tests for real persistence, fresh observation, dependency failure and fixture cleanup. Its README
documents commands and bounded evidence. These tests do not establish crash durability or hosted
monitoring. Existing historical agent evaluations do not establish effectiveness of these new samples.
