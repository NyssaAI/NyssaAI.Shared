# Prompt: Plan and Build `@nyssa-ai/db-gateway` (TypeScript)

Plan and build **`@nyssa-ai/db-gateway`** in `nyssa-ai-db-gateway/`, the TypeScript equivalent of `NyssaAI.DbGateway` (`NyssaAI.DbGateway/src/`). It must maintain full architectural, behavioral, and wire/storage parity with the C# package while adhering strictly to `.nyssa-ai/dev-standards/typescript/`.

### 1. Required Reading Before Planning
1. **TypeScript Dev Standards:**
   - `.nyssa-ai/dev-standards/typescript/rules/repo-standards.md`
   - `.nyssa-ai/dev-standards/typescript/rules/architecture-standards.md`
   - `.nyssa-ai/dev-standards/typescript/rules/standards.md`
   - `.nyssa-ai/dev-standards/typescript/references/logging.md`
   - `.nyssa-ai/dev-standards/typescript/references/testing.md`
   - `.nyssa-ai/dev-standards/typescript/references/code-smells.md`
   - `.nyssa-ai/dev-standards/typescript/examples/persistence/README.md`
2. **Existing TypeScript Sibling Package (for packaging, scripts, tsconfig, and config store integration):**
   - `nyssa-ai-configuration/` (`package.json`, `tsconfig.json`, `src/index.ts`)
3. **Reference C# Implementation (source of truth for behavior & contracts):**
   - `NyssaAI.DbGateway/src/IDbGateway.cs`, `IDbExecutor.cs`, `DbDialect.cs`
   - `NyssaAI.DbGateway/src/Transactions/IDbTransaction.cs`, `DbTransactionScope.cs`
   - `NyssaAI.DbGateway/src/Commands/` (`IDbNonQuery.cs`, `IDbCommand.cs`, `IDbQuery.cs`, `IParameterBinder.cs`, `DbParameterBinder.cs`, `IDataRecordReader.cs`, `DbRecordReader.cs`)
   - `NyssaAI.DbGateway/src/Providers/` (`IDbProvider.cs`, `DbProviderSqlite.cs`, `DbProviderPostgres.cs`, `DbProviderRegistry.cs`)
   - `NyssaAI.DbGateway/src/Configuration/` (`DbGatewayConfiguration.cs`, `DbGatewayConfigurationExtensions.cs`)
   - `NyssaAI.DbGateway/src/Migrations/` (`IMigrationRunner.cs`, `MigrationStep.cs`, `MigrationReleasePin.cs`, `MigrationReport.cs`, `MigrationCatalog.cs`, `MigrationRunner.cs`, `MigrationSql.cs`)

---

### 2. Core Architecture & C# Parity Requirements
- **Zero-Ceremony Single-Interface Gateway (`DbGateway` extends `DbExecutor`):**
  - Application code depends only on `DbGateway` (or `DbExecutor` inside shared helpers).
  - No per-command DI registration, handler classes, or reflection. Commands are plain value objects/classes instantiated directly at the call site.
  - Supports ad-hoc execution (`execute`, `scalar<T>`, `querySingle<TRow>`, `query<TRow>`) and typed command execution (`execute(command: DbNonQuery)`, `execute<TResult>(command: DbCommand<TResult>)`, `querySingle<TRow>(query: DbQuery<TRow>)`, `query<TRow>(query: DbQuery<TRow>)`).
  - Supports `beginTransaction(signal?: AbortSignal)` returning a `DbTransaction` (also implementing `DbExecutor` + `commit` / `rollback` / `AsyncDisposable`) and `executeInTransaction<T>(operation, signal?)` with automatic commit/rollback.
- **Typed Command & Coercion Boundary:**
  - `DbNonQuery`, `DbCommand<TResult>`, and `DbQuery<TRow>` carry:
    - `getSql(dialect: DbDialect): string` (allowing either universal SQL or dialect-branched SQL for `sqlite` vs `postgres`).
    - `bindParameters(binder: ParameterBinder): void`.
    - `readResult(reader: DataRecordReader, signal?: AbortSignal): Promise<TResult>` (on `DbCommand<TResult>`) or `mapRow(reader: DataRecordReader): TRow` (on `DbQuery<TRow>`).
  - **`ParameterBinder` & SQL Placeholder Normalization:**
    - Callers write `@param` placeholders in SQL and call `binder.add("param", value)` (or `@param`, normalized without prefix).
    - For SQLite (`better-sqlite3`), bind named parameters and coerce `boolean -> 1 | 0`, `Date -> ISO-8601 UTC string`, `bigint / number / string / Uint8Array / null`.
    - For PostgreSQL (`pg`), transparently rewrite `@param` tokens in SQL (ignoring single-quoted literals, double-quoted identifiers, `--` line comments, and `/* */` block comments) into positional `$1, $2, ...` parameters while preserving native types.
  - **`DataRecordReader`:**
    - Provides index-based and column-name-based typed getters (`isNull`, `getBoolean`, `getInt32`, `getInt64`, `getDouble`, `getDecimal`, `getString`, `getGuid`, `getDateTimeOffset`, `getBytes`) that normalize differences between SQLite (`0/1` booleans, ISO-8601 strings, UUID strings/16-byte blobs) and PostgreSQL (`pg` row values) into consistent TypeScript types.
- **Dual-Engine Providers (`DbProviderSqlite` & `DbProviderPostgres`):**
  - `DbDialect`: `'sqlite' | 'postgres'`.
  - Provider aliases matched case-insensitively: `'sqlite'` and `'postgres' | 'postgresql' | 'npgsql'`.
  - SQLite connection validation ensures `Data Source=...`, `:memory:`, or file path is valid, creates parent directories for file-backed DBs, and enables `PRAGMA foreign_keys = ON;`.
- **Wire-Compatible Configuration (`@nyssa-ai/configuration` Integration):**
  - Uses namespace `'nyssa.dbgateway'`.
  - Schema matches C# `DbGatewayConfiguration` JSON wire format:
    ```ts
    export interface DbGatewayConfiguration {
      readonly databaseProvider: string;
      readonly connectionString: string;
      readonly commandTimeoutSeconds: number;
    }
    ```
  - Provide helper functions on `ConfigurationStore` (`getDbGatewayConfiguration`, `setDbGatewayConfiguration`, `createDbGatewayFromConfiguration`) matching `DbGatewayConfigurationExtensions.cs`.
- **Wire-Compatible Paired & Universal Migrations (`MigrationRunner` & `MigrationCatalog`):**
  - Uses the exact same history table name and schema as C#:
    `nyssa_dbgateway_migrations (number BIGINT PRIMARY KEY, fingerprint TEXT NOT NULL)`
  - Uses the exact same SHA-256 fingerprint formula as C# (`MigrationStep.ComputeFingerprint()`):
    `"{number}:{upSql.length}:{upSql}:{downSql !== undefined && downSql !== null ? downSql.length : -1}:{downSql ?? ''}"` encoded as UTF-8 and hashed to lowercase hex.
  - Supports both universal SQL steps and paired dialect-specific steps (`.sqlite.sql` + `.postgres.sql` or programmatic definitions), plus release pin files (`migrations.lock`).
  - Concurrency locking: PostgreSQL session-level `pg_advisory_lock(564535375002)` and SQLite `BEGIN IMMEDIATE` transaction serialization.
  - Supports `migrateUp`, `migrateTo(targetVersion)`, `verify` (strict vs non-strict), and `status`.

---

### 3. Mandatory `.nyssa-ai/dev-standards/typescript` Rules
- **`TS-FILE` & `TS-STRUCTURE`:**
  - One primary exported class, interface, function, or schema per `.ts` file, named after the primary export (`PascalCase.ts` for types/classes/interfaces, `camelCase.ts` for functions, or feature-local `Types.ts` for pure readonly DTOs/unions).
  - Keep every file under 350 lines (aim for <100 lines).
  - Do NOT create banned catch-all directories or files (`contracts`, `shared`, `utils`, `helpers`, `common`, `misc`, `types`, `models`).
  - Separate tests into `nyssa-ai-db-gateway/test/unit/` (runs without Docker) and `nyssa-ai-db-gateway/test/use_case/` (runs against SQLite + PostgreSQL).
- **`TS-HEADER`:**
  - Every authored `.ts` file must begin with `// Responsibility: <summary>` and `// Public interface:` followed by an ASCII tree (`+-- ` / `` `-- ``) matching exported symbols.
- **`TS-NAME`:**
  - Do NOT prefix TypeScript interfaces with `I` (`DbGateway`, `DbExecutor`, `DbTransaction`, `DbNonQuery`, `DbCommand`, `DbQuery`, `ParameterBinder`, `DataRecordReader`, `MigrationRunner`, `DbProvider`).
  - Use entity-first naming (`DbProviderSqlite`, `DbProviderPostgres`, `DbGatewayClient`, `DbTransactionScope`).
- **`TS-STRICT` & `TS-DATA`:**
  - `strict: true`, `noUncheckedIndexedAccess: true`, `exactOptionalPropertyTypes: true`, `verbatimModuleSyntax: true`.
  - Zero `any` (`@typescript-eslint/no-explicit-any`).
  - Use `readonly` properties and `readonly T[]` arrays on all data interfaces/DTOs.
- **`TS-LOG` & `TS-FAILURE` & `TS-CONSTRUCTION`:**
  - Constructor-inject dependencies (including a structured logger interface with safe sink containment and secret redaction for connection strings).
  - Emit structured operational boundary logs with monotonic `performance.now()` duration (`elapsedMs`) and outcome without leaking connection string passwords or parameter payloads.
  - Propagate `AbortSignal` across async operations.

---

### 4. Deliverables & Verification Steps
1. **Plan First:** Inspect the files listed in Section 1 and present a concise implementation plan listing the proposed directory tree, public interfaces, and verification steps.
2. **Implementation:** Build `nyssa-ai-db-gateway/` and register `"nyssa-ai-db-gateway"` in `.nyssa-ai/dev-standards/typescript.json` (`owned_roots`, `build_commands`, `unit_test_commands`, `use_case_test_commands`) and root test scripts (`run-tests.ps1`, `README.md`).
3. **Verification:**
   - Run `node .nyssa-ai/dev-standards/typescript/scripts/check.mjs nyssa-ai-db-gateway`
   - Run `python .nyssa-ai/dev-standards/typescript/scripts/check_adoption.py . --run`
   - Run both `test/unit` and `test/use_case` suites (including cross-checking that a migration applied by C# or with the C# fingerprint format is recognized identically by TypeScript).
