# Python standards — 0.6.3

> [!IMPORTANT] Scope Directive
> Authoritative developer reference guide, testing contracts, and operational standards for PYTHON systems in dev-standards. It does NOT impose rules on unowned languages.

Jeremy Maynor's rules, observable samples and bounded structural checks. This package is self-contained;
it has no dependency on sibling language packages, the old combined plugin, or archived `.temp` content.

## Rules and examples

Read the [common repository organization rules](rules/repo-standards.md), [cross-language architectural rules](rules/architecture-standards.md), [the language rules](rules/standards.md) and the [sample walkthrough](examples/README.md).
The rules explicitly include all five SOLID principles. Pattern selection and responsibility
cohesion remain review decisions. No 98% reliability or agent-speed improvement is claimed.

## Testing and executable documentation

The [testing contract](testing.md) requires fast unit tests, complete public use-case
coverage and real Docker-hosted dependencies for infrastructure-backed behavior. Tests teach the
public API through simple, idiomatic Arrange / Act / Assert code. Use proper patterns where
needed; prefer small duplication to abstractions that hide the scenario. Fixtures own infrastructure
lifecycle, isolation and cleanup; business actions and assertions stay visible in the tests.

The pure examples need no Docker. The [PostgreSQL workflow](examples/persistence/README.md)
adds explicit local use-case tests with fixture-owned infrastructure, a fresh persistence observer
and captured operational records. The structural checker cannot certify test quality or KISS.

## Code smells and anti-patterns

The [review catalog](code-smells.md) pairs production/test failure modes with their
consequences, simplest corrections and valid counterexamples. Review only relevant entries;
source evidence and rule authority determine findings, not a smell count. Structural checks do
not establish these semantic properties.

## Mandatory operational logging

The [logging contract](logging.md) requires useful evidence for early warning, diagnosis
and verification of fixes at operational boundaries. It covers structured outcomes, correlation,
deployed version, degradation signals, safe causes, redaction and bounded sink-failure behavior.
Each changed boundary needs captured-record tests and a short diagnostic recipe.

The structural checker does not verify logging, and the current pure value/formatter samples do
not establish logging behavior. The PostgreSQL workflow demonstrates bounded local logging and
sink-failure evidence; its README names the remaining limits. Verify the target repository's
actual sink and filters; logging in every function or a new logging framework is not required.

## Verify this package

Prerequisites: Python 3.12+ with `ruff`, `mypy`, and `pytest`.
Run from the repository root:

```text
python scripts/verify.py
```

This runs the AST structural checker (`scripts/check.py`) against the examples, checks formatting and lint rules with `ruff`, type-checks with `mypy --strict`, runs the checker and adoption unit controls, executes the example behavioral tests, and runs the console demo.

## Check a target repository

```text
python scripts/check.py /path/to/owned/source
```

`--describe` after the root prints public-tree JSON without editing files. Trees use lexically
ordered siblings and parameter names (omitting `self` and `cls`). Feature-owned pure DTO files named `types.py`
may group multiple immutable data types, enums, and `NewType` identifiers without behavioral methods;
additional unrelated behavioral classes or functions fail.
Describe mode skips header comparison only; other findings remain nonzero. Unknown flags fail
with usage status 2. JSON goes to stdout and diagnostics to stderr.

Run the target's own linter, type-checker and tests too. Passing the package's sample suite is not verification of
your target repository. The default check reads source without rewriting it, rejects empty source roots,
reports file/rule details, and returns nonzero on defects or unsupported inspected surfaces.
Use an explicit owned-source root. Dot folders and generated build/cache folders are excluded;
source symlinks are rejected. Review files outside that declared root separately.

## Update public headers explicitly

```text
python scripts/check.py /path/to/owned/source --update-headers
```

This opt-in command reuses the checker's declaration inventory to update a recognized ASCII tree.
It preserves authored responsibility text, source bodies and newline style. It does not invent a
responsibility statement. Unsupported surfaces and unrecognized headers remain unchanged for review.
Review the diff, then rerun the ordinary checker; other violations still fail. Caches and excluded
source folders retain the same scanning rules as the default check.

## Actual coverage

Checks use the Python `ast` parser: top-level concept/type count, `snake_case` primary-export filenames,
`types.py` pure-DTO grouping, `Protocol` naming without an `I` prefix, responsibility-header presence,
exact explicitly declared public-member trees, forbidden folders, the absence of `Any`, parameter/return
type annotations, and named-layer imports. Private (`_`-prefixed) declarations and members are excluded.
Properties appear as attributes; `__init__` appears as `new(...)`.
Star imports (`from ... import *`) and dynamic export surfaces produce explicit unsupported findings
(`PY-COVERAGE`) rather than silently passing.

Layer detection uses the selected root's name and directories below it, never ancestor directories
outside the scan. Select a root that contains the relevant architectural layout. Responsibility
comments must be nonempty; their truth and usefulness are reviewed, not measured by character count.

Not checked: semantic alias resolution, complete exported package surfaces, every docstring rule,
deeply nested/inferred runtime types, deeply immutable runtime objects, asynchronous task lifetimes,
or semantic SOLID. The small local `Result` carries only this sample's validation code; it is not
an approved replacement for a broader production error taxonomy when more context is required.

The controls contain both passing cases and deliberate violations: wrong file names, multiple behavioral types,
forbidden dependencies, `Any` usage, `I`-prefixed `Protocol` types, and header mismatches. Their success is
checker evidence, not agent implementation evidence. Semantic SOLID and rule exceptions require review.

## Local repository adoption

This package exposes no skills, slash commands, hooks or MCP tools. Its rules are the authority;
examples illustrate them and scripts check only the coverage listed above. Nothing automatically
starts an implementation flow.

Link `rules/repo-standards.md`, `rules/architecture-standards.md`, and `rules/standards.md` from the consuming repository's `AGENTS.md` (or its agent's equivalent),
declare the owned source roots, and record the target build/test and checker commands. Resolve the
package location explicitly; do not rely on an old installed skill name. Run the deterministic
checks locally when reviewing relevant changes; do not wire this package into CI. A Markdown instruction cannot prove full conformance.

Read examples only when the design calls for them. Operational boundaries also use
`references/logging.md`; review captured records and the diagnostic recipe because the structural
checker cannot establish logging behavior.

This independent source bundle is also included in the `dev-standards@nyssaai` version 0.5.1
distribution. Adopt the whole language directory at a pinned revision so its relative links and
scripts remain valid. Installation makes the files available; repository instructions and explicit local checks
activate the standards. Adopt and version this language package independently.

Start with the [local adoption walkthrough](adoption/README.md) for a pinned copy, an agent-instruction fragment and explicit verification commands.
