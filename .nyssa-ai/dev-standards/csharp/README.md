# C# / .NET standards — 0.6.3

> [!IMPORTANT] Scope Directive
> Authoritative developer reference guide, testing contracts, and operational standards for CSHARP systems in dev-standards. It does NOT impose rules on unowned languages.

Jeremy Maynor's rules, observable samples and bounded structural checks. This package is self-contained;
it has no dependency on sibling language packages, the old combined plugin, or archived `.temp` content.

## Rules and examples

Read the [common repository organization rules](rules/repo-standards.md), [the language rules](rules/standards.md) and the [sample walkthrough](examples/README.md).
The rules explicitly include all five SOLID principles. Pattern selection and responsibility
cohesion remain review decisions. No 98% reliability or agent-speed improvement is claimed.

## Testing and executable documentation

The [testing contract](testing.md) requires fast unit tests, complete public use-case
coverage and real Docker-hosted dependencies for infrastructure-backed behavior. Tests teach the
public API through simple, idiomatic Arrange / Act / Assert code. Use proper patterns where
needed; prefer small duplication to abstractions that hide the scenario. Fixtures own infrastructure
lifecycle, isolation and cleanup; business actions and assertions stay visible in the tests.

The pure examples need no Docker. The [PostgreSQL workflow](examples/Persistence/README.md)
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

Prerequisites: Python 3.12+, Git, .NET SDK 10 and an authorized Nyssa.Shared checkout.
Run from the repository root:

Set `NYSSA_SHARED_ROOT` to your Nyssa.Shared checkout, then run:

```text
python scripts/verify.py
```

PowerShell example: `$env:NYSSA_SHARED_ROOT = 'D:/repos/jamaynor/Nyssa.Shared'`.
The script reads the exact revision in [dependency-lock.json](../../../../scripts/dependency-lock.json) through
`git archive`, builds a copy under the package's `.temp`, and does not modify the checkout.
The canonical Guard APIs are not available from public NuGet; this prerequisite is explicit.
A checkout missing that revision fails before verification. No illustrative Result implementation
is substituted. The verification script runs the demo after xUnit tests.


## Check a target repository

```text
dotnet run --project scripts/Checker/Checker.csproj -- /path/to/owned/source
```

Optional second argument: a JSON object mapping source-root-relative grouping files to reasons:
`{"Messages.cs":"Small related passive DTOs in the same namespace."}`.
Only multiple simple expanded-body record DTOs or enums in one namespace qualify mechanically.
Behavior, interfaces, nested types, positional records and different namespaces reject the exception.
The reason, meaningful grouping name, relatedness and whether the set is sufficiently numerous
remain review decisions. Unused exception entries fail. Namespace-free grouping is rejected.
Grouping compares the full namespace, including nested namespace declarations, and accepts equivalent
nested/dotted spellings. Reasons must be nonempty; no arbitrary character count stands in for review.


Run the target's own compiler/tests too. Passing the package's sample suite is not verification of
your target repository. The checkers read source without rewriting it, reject empty source roots,
report file/rule details, and return nonzero on defects or unsupported inspected surfaces.
Use an explicit owned-source root. Dot folders and generated build/dependency folders are excluded;
source symlinks are rejected. Review files outside that declared root separately.

## Actual coverage

Checks parse C# with the SDK's Roslyn libraries: one type/file and matching name, bounded DTO/enum
grouping, no positional records/primary constructors, I-prefixed interfaces, forbidden folder names,
and direct qualified references to named outer layers. XML documentation, nullable analysis and
warnings-as-errors run on the sample build. Tests use the real pinned Nyssa.Result.
Primary-constructor checks cover both classes and structs. Layer detection includes the selected
root's own name (for example Domain) and directories beneath it, not unrelated ancestor directories.

Not checked: complete semantic dependency resolution, every naming rule, full immutability,
all async/cancellation contracts, every public export, DI registration behavior, architecture
through aliases or reflection, or SOLID judgment. The checker is not a compiler replacement.


The controls contain both passing cases and deliberate violations: wrong file names, multiple types,
forbidden dependencies, and the language-specific header or grouping cases. Their success is
checker evidence, not agent implementation evidence. Semantic SOLID and rule exceptions require review.

## Local repository adoption

This package exposes no skills, slash commands, hooks or MCP tools. Its rules are the authority;
examples illustrate them and scripts check only the coverage listed above. Nothing automatically
starts an implementation flow.

Link both `rules/repo-standards.md` and `rules/standards.md` from the consuming repository's AGENTS.md (or its agent's equivalent),
declare the owned source roots, and record the target build/test and checker commands. Resolve the
package location explicitly; do not rely on an old installed skill name. Run the deterministic
checks locally when reviewing relevant changes; do not wire this package into CI. A Markdown instruction cannot prove full conformance.

Read examples only when the design calls for them. Operational boundaries also use
`references/logging.md`; review captured records and the diagnostic recipe because the structural
checker cannot establish logging behavior.

This independent source bundle is also included in the `dev-standards@jamaynor` version 0.5.0
distribution. Adopt the whole language directory at a pinned revision so its relative links and
scripts remain valid. Installation makes the files available; repository instructions and explicit local checks
activate the standards. Adopt and version this language package independently.

Start with the [local adoption walkthrough](adoption/README.md) for a pinned copy, an agent-instruction fragment and explicit verification commands.
