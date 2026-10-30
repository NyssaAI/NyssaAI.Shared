# Common repository organization rules

> [!IMPORTANT] Scope Directive
> Defines the mandatory language-agnostic single-project and monorepo layout, `.temp/` scratch directory discipline, `.nyssa-ai/` and `.compound-engineering/` configuration namespaces, unified `docs/` hierarchy, test isolation, and feature-group alignment rules for owned repositories. It ensures seamless coexistence with `plan-product` and `compound-engineering`; cross-language architectural invariants live in [`architecture-standards.md`](architecture-standards.md), and language-specific folder casing, module/project boundaries, and type placement are governed by the respective language rulebooks ([`csharp-standards.md`](csharp-standards.md), [`go-standards.md`](go-standards.md), [`python-standards.md`](python-standards.md), [`typescript-standards.md`](typescript-standards.md)).

- **REPO-ROOT:** A single-project repository uses a clean, canonical top-level directory layout: `src/` for production source code, `test/` for isolated automated tests (`test/unit/`, `test/use_case/`, and optional `test/e2e/`), `docs/` for durable product and engineering documentation, and `scripts/` for repository lifecycle and automation scripts. A monorepo uses `apps/<owned-app>/` and `tools/<owned-tool>/` as its owned-project roots; each root independently uses that same `src/` and `test/unit/` / `test/use_case/` / `test/e2e/` layout and owns its language/runtime manifest (for example, `pyproject.toml`, `package.json`, or a `.csproj`). The monorepo policy applies separately to every owned app or tool; it does not require a common language, framework, or build system. Repositories that package agent plugins, skills, or evaluation suites may also include canonical distribution surfaces (`skills/`, `commands/`, `hooks/`, `evals/`, `assets/`, `site/`). Do not scatter loose scripts at the repository root, co-mingle test files inside an owned project's `src/`, create competing `tests/` or `__tests__/` directories at a project root, or create catch-all folders (`contracts`, `shared`, `utils`, `helpers`, `common`, `misc`, `types`, `models`).
- **REPO-SCRATCH:** `.temp/` is the single mandatory repository-local scratch, build-staging, and temporary output directory across all languages, scripts, test fixtures, and agent workflows. Ignore `.temp/` in `.gitignore`. Never create ad-hoc `tmp/`, `temp/`, `scratch/`, or `.scratch/` directories at the repository root or inside source trees. Tool-specific dot-directories (`.git/`, `.github/`, `.nyssa-ai/`, `.compound-engineering/`, `.codegraph/`, `.context/`, `.claude/`, `.cursor/`, `.agents/`, etc.) are permitted as needed by their respective tools; reserve `.context/` strictly for repo-bound, user-curated Compound Engineering artifacts.
- **REPO-CONFIG:** Store Nyssa AI repository configurations and pinned standards bundles under `.nyssa-ai/`:
  - `.nyssa-ai/dev-standards/<language>/` and `.nyssa-ai/dev-standards/<language>.json` hold the pinned language standards bundle (`rules/`, `references/`, `examples/`, `scripts/`) and the repository's local verification configuration. In a monorepo, a language manifest enumerates every owned source root governed by that language (for example, `apps/sales-researcher/src` and `tools/crm/src`). Do not add a language to CI enumeration until its pinned bundle and repository-level manifest exist.
  - `.nyssa-ai/plan-product/` holds the `plan-product` path authority (`config.json`), schema (`schema.json`), and document templates (`templates/`).
  - `.compound-engineering/` holds Compound Engineering configuration (`config.yaml` for tracked team defaults including optional `docs_root`, `config.local.yaml` for checkout-local overrides, and `config.example.yaml`).
- **REPO-DOCS:** Maintain a unified `docs/` hierarchy (or the `docs_root` configured in `.compound-engineering/config.yaml`) where `plan-product` (Model v2) and `compound-engineering` artifacts coexist without path collisions:
  - **Product planning (`plan-product`, governed by `.nyssa-ai/plan-product/config.json`):**
    - `docs/product.md` — Product identity, target audience, core problem, solution shape, boundaries, and success signals.
    - `docs/features.md` — Feature checklist summary and plain-language feature inventory organized by Feature Group (`F-gg.nn`).
    - `docs/tech-stack.md` — Runtime languages, frameworks, storage, hosting, and architectural unknowns.
    - `docs/concepts/c-nn-short-slug.md` — Atomic domain models and cross-feature macro-flows (`C-nn`).
    - `docs/features/f-gg.nn/feature.md` — Feature boundary specification, inputs, outputs, and acceptance checks (`F-gg.nn`).
    - `docs/features/f-gg.nn/uc-gg.nn.uu-short-slug.md` — Actor workflows, entry routes, and ASCII process trees (`UC-gg.nn.uu`).
    - `docs/roadmap.md` — Dependency-ordered technical delivery phases and steps (`R-pp.ss`).
  - **Engineering plans & compound knowledge (`compound-engineering`):**
    - `docs/plans/` — Unified requirements and implementation plans (`ce-brainstorm` and `ce-plan`).
    - `docs/solutions/` — Categorized institutional learnings (`ce-compound`) with YAML frontmatter (`module`, `tags`, `problem_type`, or `title`/`applies_when`).
    - `docs/brainstorms/` — Historical brainstorm inputs and specialized feedback analyses (such as `docs/brainstorms/riffrec-feedback/`).
    - `docs/guides/` and `docs/specs/` — User-facing guides, configuration catalogs, and target specifications when applicable. `docs/specs/` is the durable home for versioned cross-language contracts, including JSON Schema; do not create a generic root `contracts/` catch-all.
- **REPO-CONTRACT-TOOL:** A contract-validation tool is an owned tool (for example, `tools/contract-check/`) and follows its project's language and test rules. It may expose both CLI and MCP interfaces, but every interface must validate the same versioned schemas in `docs/specs/`; do not fork or duplicate contract definitions by transport.
- **REPO-GOVERNANCE:** `AGENTS.md` is the canonical root agent-instructions file, read natively by OpenAI Codex, Hermes Agent, Google Antigravity (`agy`), Cursor, OpenClaw, Windsurf, Amp, and OpenCode. Host-specific instruction files must never maintain diverging standalone instructions:
  - **Claude Code (`CLAUDE.md`):** Root `CLAUDE.md` (when present) must be a symlink to `AGENTS.md` (or import `@./AGENTS.md` / match `AGENTS.md` byte-for-byte on filesystems without symlink support) so Claude Code and strict plugin validators share one source of truth. Optional modular rules live under `.claude/rules/`.
  - **Google Gemini CLI & Antigravity (`GEMINI.md`):** Root `GEMINI.md` (when present, or referenced by `gemini-extension.json`) must either symlink to `AGENTS.md` or import it via `@./AGENTS.md`.
  - **Hermes Agent & Codex overrides (`.hermes.md`, `HERMES.md`, `AGENTS.override.md`):** Hermes Agent evaluates `.hermes.md`, `HERMES.md`, and `AGENTS.override.md` ahead of `AGENTS.md` (and Codex evaluates `AGENTS.override.md` ahead of `AGENTS.md`). Do not commit diverging override files that fork repository instructions away from `AGENTS.md`.
  - **IDE & Copilot adapters (`.cursorrules`, `.cursor/rules/`, `.github/copilot-instructions.md`):** Keep `AGENTS.md` authoritative; when host-specific adapter files are required, point them to `AGENTS.md` rather than duplicating rule bodies.
  - **Standard governance & domain docs:** When present at the repository root, use canonical filenames: `README.md`, `STRATEGY.md`, `CONCEPTS.md`, `CODING_STANDARDS.md`, `CHANGELOG.md`, and `LICENSE`.
- **REPO-GITIGNORE:** Maintain a root `.gitignore` that explicitly preserves repository-owned plugin configuration, pinned standards, and durable planning/engineering artifacts in version control while ignoring machine-local scratch, caches, and checkout-local overrides:
  - **Must remain tracked and checked in (never blanket-ignored):**
    - `.nyssa-ai/dev-standards/` — Pinned language standards bundles (`.nyssa-ai/dev-standards/<language>/`) and local verification manifests (`.nyssa-ai/dev-standards/<language>.json`).
    - `.nyssa-ai/plan-product/` — Planning path authority (`config.json`), schema (`schema.json`), and document templates (`templates/`).
    - `.compound-engineering/config.yaml` and `.compound-engineering/config.example.yaml` — Tracked team defaults and documented configuration example (never ignore the entire `.compound-engineering/` directory).
    - `docs/` — Durable `plan-product` specifications (`docs/product.md`, `docs/features.md`, `docs/tech-stack.md`, `docs/roadmap.md`, `docs/concepts/`, `docs/features/`) and `compound-engineering` pipeline artifacts (`docs/plans/`, `docs/solutions/`, `docs/brainstorms/`, `docs/guides/`, `docs/specs/`).
    - `AGENTS.md` and tracked host symlinks/delegates (`CLAUDE.md`, `GEMINI.md`).
  - **Must be ignored in `.gitignore`:**
    - `.temp/` — The single repository-local scratch and staging directory (`REPO-SCRATCH`).
    - `.compound-engineering/*.local.yaml` — Machine-local checkout overrides (such as `.compound-engineering/config.local.yaml`).
    - `.context/compound-engineering/` — Machine-local run state for `ce-optimize`, `ce-prototype`, and `ce-polish` (while allowing curated repo-bound files directly under `.context/` if used).
    - `.nyssa-ai/plan-product/usage/` — Ephemeral local usage telemetry (if generated).
    - `.worktrees/` — Local git worktrees created by `ce-worktree`.
    - Language build and dependency outputs (`node_modules/`, `bin/`, `obj/`, `dist/`, `__pycache__/`, `*.py[cod]`, `.pytest_cache/`).
  - **Canonical `.gitignore` baseline:**
    ```gitignore
    # Build, dependency, and test runner outputs
    __pycache__/
    *.py[cod]
    .pytest_cache/
    node_modules/
    bin/
    obj/
    dist/

    # Mandatory repository-local scratch directory (REPO-SCRATCH)
    .temp/
    .worktrees/

    # Plugin state: keep .nyssa-ai/dev-standards/, .nyssa-ai/plan-product/, and
    # .compound-engineering/config.yaml tracked; ignore only machine-local state
    !.nyssa-ai/
    !.nyssa-ai/dev-standards/
    !.nyssa-ai/plan-product/
    .nyssa-ai/plan-product/usage/
    !.compound-engineering/
    .compound-engineering/*.local.yaml
    .context/compound-engineering/
    ```
- **REPO-TEST-LAYOUT:** Within each owned project, separate automated tests by execution layer under `test/` using singular canonical folder names:
  - `test/unit/` — Fast, deterministic in-process unit and isolated component tests requiring no Docker, database, network services, or headless browser (never `unit_test/` or `unit_tests/`).
  - `test/use_case/` — Complete caller workflow and service/database integration tests (`UC-gg.nn.uu`) exercising public handlers, repositories, and real local/Docker-backed dependencies without a browser (never `use_cases/`).
  - `test/e2e/` — Optional full-stack browser or system end-to-end tests (such as **Playwright** driving a real headless browser against a running web/desktop application across multi-page user journeys).
  Mirror the feature organization of that project's `src/` inside `test/unit/` and `test/use_case/`, and organize `test/e2e/` by user journey or feature group.

## Design decisions (review required)

- **REPO-FEATURES:** Prefer aligning feature folders under `src/` (and mirrored suites under `test/unit/` and `test/use_case/`) with cohesive **Feature Groups** (`F-gg` in `docs/features.md`) or shared domain concepts (`C-nn` in `docs/concepts/`) rather than creating a separate top-level folder per individual feature ID (`F-gg.nn`). When a feature group or anchor feature defines a cohesive domain area, keep its subordinate features within that same feature folder. Start flat inside each feature folder and introduce inner layers (`Domain`/`Application`/`Infrastructure` or `domain`/`application`/`infrastructure`/`api`/`ui`) only when earned by distinct reasons to change. Treat feature-group folder alignment as an architectural preference evaluated during design and review, not a rigid mechanical naming lock.

## Language-specific structure rules

Apply this common repository layout and the [cross-language architectural rules](architecture-standards.md) separately to each owned project together with its active language rulebook. The listed `src/` and `test/` paths are relative to that project root in a monorepo:

| Language / Framework | Language Rulebook | Folder Casing & Module Conventions |
| :--- | :--- | :--- |
| **C# / .NET** | [`csharp-standards.md`](csharp-standards.md) (`CS-STRUCTURE`, `CS-FILE`, `CS-BOUNDARY`, `CS-TEST`) | `PascalCase` feature and layer folders (`Domain`, `Application`, `Infrastructure`, `Api`, `Ui`) matching namespaces and `.csproj` names; shared build properties in `Directory.Build.props`; `I`-prefixed interfaces. |
| **Go** | [`go-standards.md`](go-standards.md) (`GO-STRUCTURE`, `GO-FILE`, `GO-BOUNDARY`, `GO-TEST`) | `camelCase` feature folders and package names (single words lowercase), runnable entrypoints under `cmd/`, private split packages under `internal/`, acyclic imports, direct interface names without `I` prefix. |
| **Python** | [`python-standards.md`](python-standards.md) (`PY-STRUCTURE`, `PY-FILE`, `PY-BOUNDARY`, `PY-TEST`) | `snake_case` feature and layer packages (`domain`, `application`, `infrastructure`, `api`, `ui`), `snake_case.py` modules named for their primary export (`types.py` allowed for pure DTOs), `Protocol` contracts without `I` prefix. |
| **TypeScript / React** | [`typescript-standards.md`](typescript-standards.md) (`TS-STRUCTURE`, `TS-FILE`, `TS-BOUNDARY`, `TS-TEST`) | `camelCase` feature and layer folders (`domain`, `application`, `infrastructure`, `api`, `ui`), `PascalCase` component/type files, `camelCase` function/hook files (`Types.ts`/`types.ts` allowed for pure DTOs), interfaces without `I` prefix. |
