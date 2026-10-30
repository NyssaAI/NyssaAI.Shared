# Observable Python samples

> [!IMPORTANT] Scope Directive
> Concrete executable reference implementations and pattern examples for PYTHON in dev-standards. It does NOT provide generic programming tutorials.

## What to run and observe

Use the commands in the [package guide](../README.md). Verification ends by printing:

```text
normalized: Ada@example.com
email: Ada@example.com
{"email":"Ada@example.com"}
rejected: email.invalid
```

1. Parse an address before construction. Preserve local-part case and normalize domain case.
2. Pass the same validated value through text and JSON formatters using one provider `Protocol` contract (`EmailFormatter`).
3. Parse invalid input and observe an actual returned `Result` failure (`Err("email.invalid")`).
4. Run the same behavioral contract against both providers. JSON also has a structural content test.

The actual requirement has two output representations; this earns a small strategy/provider seam.
A caller needing only plain text can call a plain formatter without introducing a protocol.
The value-only example needs no service, Builder, covering protocol or I/O abstraction.

## Responsibilities and SOLID

Address construction owns validation. Formatters own representation. The demo owns composition and
presentation of outcomes. A new provider can satisfy the same `EmailFormatter` protocol without modifying existing
formatters. The contract requires preserving the normalized address, no input mutation and no I/O.
Providers have no unrelated methods; the caller depends on that narrow protocol.

## Scope

The address policy deliberately accepts a bounded ASCII subset using a simple documented expression.
It is not complete RFC email validation, domain verification or proof of deliverability. The sample
demonstrates style and contract handling, not a production email service. It performs no persistence,
network request or transaction; it makes no claims about those boundaries. Existing sample tests
do not prove all design rules or the full language profile.

Operational logging is specified in the [logging contract](../logging.md). These pure
value/formatter examples intentionally do not log internally. Their displayed values are tutorial
output, not an approved operational event schema or evidence of production sink behavior.

## Provenance

Adapted from Jeremy Maynor's Email/EmailAddress examples in dev-standards across C#, Go, and TypeScript.
This Python version applies `PY-FILE`, `PY-HEADER`, `PY-NAME` (`Protocol` without an `I` prefix and `types.py` for grouped DTOs),
`PY-DATA` (`@dataclass(frozen=True, slots=True)` and `NewType`), and `PY-FAILURE` (discriminated-union `Result`).

## Tests as how-to examples

Follow the [testing contract](../testing.md). Tests use explicit public construction,
ordinary calls and direct assertions. Provider contract cases share the same behavioral checks;
format-specific expectations have their own tests. Keep unit tests independent of Docker.
When adding infrastructure-backed behavior, provide a separate use-case suite with disposable
real dependencies and fixtures for setup/teardown. Do not hide the business workflow in fixtures
or build a generic testing framework for a few examples.

## Operational persistence workflow

The separate [PostgreSQL registration example](persistence/README.md) demonstrates feature DTO grouping in [`types.py`](persistence/types.py),
real persistence workflow boundaries, structured operational logs, degradation warnings, and dependency/sink failures.
