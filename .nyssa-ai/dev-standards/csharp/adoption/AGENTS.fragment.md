> [!IMPORTANT] Scope Directive
> Reusable agent instruction fragment for adopting dev-standards in a consumer repository.

# Development standards (csharp)

Read `.nyssa-ai/dev-standards/csharp/rules/repo-standards.md`,
`.nyssa-ai/dev-standards/csharp/rules/architecture-standards.md` and
`.nyssa-ai/dev-standards/csharp/rules/standards.md` before changing owned csharp code or repository layout.
The pinned source revision, owned roots and local commands are recorded in
`.nyssa-ai/dev-standards/csharp.json`. Repository requirements and explicit user instructions take
precedence; record the scope and reason of an exception in the change description.

Read relevant examples when a design choice needs one. For operational boundaries, also read
`.nyssa-ai/dev-standards/csharp/references/logging.md`. For tests and fixtures, read
`.nyssa-ai/dev-standards/csharp/references/testing.md`. Apply relevant entries of
`.nyssa-ai/dev-standards/csharp/references/code-smells.md` with source evidence and counterexamples.

Run applicable build, unit and use-case commands locally. Unit tests must work without Docker.
A requested infrastructure test requires Docker; report missing prerequisites as blocked,
never as a pass. Use the structural checker as local feedback and report unsupported coverage
separately from rule violations. A green checker does not establish complete conformance.
Do not add CI wiring, hooks or automatic standards execution.

Validate adoption with:

```text
python .nyssa-ai/dev-standards/csharp/scripts/check_adoption.py .
```

Update the pinned local bundle from the installed `dev-standards` plugin with:

```text
python .nyssa-ai/dev-standards/csharp/scripts/check_adoption.py . --sync
```

Execute the configured local build and unit commands plus the structural checks with `--run`.
Add `--use-case` to run the configured infrastructure suite explicitly. Review command definitions
before executing them; they are owned by this repository.
