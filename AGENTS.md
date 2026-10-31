You communicate concisely and factually as possible. You explain the architectural / strategic ideas concisely and use bullet point lists to accentuate key points.

# Repository Library Directory Naming

All library and component directories in this repository MUST follow the canonical naming convention:

`nyssa-ai-{component}-{language}`

Where:
- `{component}` is the lowercase component name without hyphens or separators (e.g., `configuration`, `dbgateway`, `logging`, `result`).
- `{language}` denotes the implementation runtime:
  - `-cs` for C# / .NET libraries (e.g., `nyssa-ai-dbgateway-cs`, `nyssa-ai-configuration-cs`)
  - `-ts` for TypeScript / Node.js libraries (e.g., `nyssa-ai-configuration-ts`)
  - `-py` for Python libraries (e.g., `nyssa-ai-configuration-py`)

Any additional library or package added to this repository must follow this directory naming rule:
- Production code lives under `nyssa-ai-{component}-{language}/src/`.
- Isolated test suites live under `nyssa-ai-{component}-{language}/test/unit/` and `nyssa-ai-{component}-{language}/test/use_case/`.
- Internal project files, package identifiers, and assemblies preserve their canonical identity and casing (e.g., `NyssaAI.DbGateway.csproj`, `NyssaAI.DbGateway.dll`), while the directory itself strictly uses `nyssa-ai-{component}-{language}`.
- Pinned language manifests under `.nyssa-ai/dev-standards/{csharp|typescript|python}.json` must be updated with the new library's `source_roots` and commands.

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

# Development standards (typescript)

Read `.nyssa-ai/dev-standards/typescript/rules/repo-standards.md`,
`.nyssa-ai/dev-standards/typescript/rules/architecture-standards.md` and
`.nyssa-ai/dev-standards/typescript/rules/standards.md` before changing owned typescript code or repository layout.
The pinned source revision, owned roots and local commands are recorded in
`.nyssa-ai/dev-standards/typescript.json`. Repository requirements and explicit user instructions take
precedence; record the scope and reason of an exception in the change description.

Read relevant examples when a design choice needs one. For operational boundaries, also read
`.nyssa-ai/dev-standards/typescript/references/logging.md`. For tests and fixtures, read
`.nyssa-ai/dev-standards/typescript/references/testing.md`. Apply relevant entries of
`.nyssa-ai/dev-standards/typescript/references/code-smells.md` with source evidence and counterexamples.

Run applicable build, unit and use-case commands locally. Unit tests must work without Docker.
A requested infrastructure test requires Docker; report missing prerequisites as blocked,
never as a pass. Use the structural checker as local feedback and report unsupported coverage
separately from rule violations. A green checker does not establish complete conformance.
Do not add CI wiring, hooks or automatic standards execution.

Validate adoption with:

```text
python .nyssa-ai/dev-standards/typescript/scripts/check_adoption.py .
```

Update the pinned local bundle from the installed `dev-standards` plugin with:

```text
python .nyssa-ai/dev-standards/typescript/scripts/check_adoption.py . --sync
```

Execute the configured local build and unit commands plus the structural checks with `--run`.
Add `--use-case` to run the configured infrastructure suite explicitly. Review command definitions
before executing them; they are owned by this repository.

# Development standards (python)

Read `.nyssa-ai/dev-standards/python/rules/repo-standards.md`,
`.nyssa-ai/dev-standards/python/rules/architecture-standards.md` and
`.nyssa-ai/dev-standards/python/rules/standards.md` before changing owned python code or repository layout.
The pinned source revision, owned roots and local commands are recorded in
`.nyssa-ai/dev-standards/python.json`. Repository requirements and explicit user instructions take
precedence; record the scope and reason of an exception in the change description.

Read relevant examples when a design choice needs one. For operational boundaries, also read
`.nyssa-ai/dev-standards/python/references/logging.md`. For tests and fixtures, read
`.nyssa-ai/dev-standards/python/references/testing.md`. Apply relevant entries of
`.nyssa-ai/dev-standards/python/references/code-smells.md` with source evidence and counterexamples.

Run applicable build/typecheck, unit and use-case commands locally. Unit tests must work without Docker.
A requested infrastructure test requires Docker; report missing prerequisites as blocked,
never as a pass. Use the structural checker as local feedback and report unsupported coverage
separately from rule violations. A green checker does not establish complete conformance.
Do not add CI wiring, hooks or automatic standards execution.

Validate adoption with:

```text
python .nyssa-ai/dev-standards/python/scripts/check_adoption.py .
```

Update the pinned local bundle from the installed `dev-standards` plugin with:

```text
python .nyssa-ai/dev-standards/python/scripts/check_adoption.py . --sync
```

Execute the configured local build and unit commands plus the structural checks with `--run`.
Add `--use-case` to run the configured infrastructure suite explicitly. Review command definitions
before executing them; they are owned by this repository.
