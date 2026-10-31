# @nyssa-ai/db-gateway

`@nyssa-ai/db-gateway` provides low-ceremony, high-performance typed SQL database access across **SQLite** (`better-sqlite3`) and **PostgreSQL** (`pg`) for Node.js and TypeScript applications, maintaining full behavioral, architectural, and wire/storage parity with `NyssaAI.DbGateway`.

## Features

- **Zero-Ceremony Gateway (`DbGateway` / `DbExecutor`):** Single entrypoint for application code without per-command DI registration or reflection.
- **Typed Command Pattern:** Reusable commands and queries (`DbNonQuery`, `DbCommand<TResult>`, `DbQuery<TRow>`) encapsulating dialect SQL, parameter binding, and result mapping.
- **Transparent Parameter Coercion:** Write SQL with standard `@param` placeholders. SQLite binds named parameters with type coercion (`boolean -> 1|0`, `Date -> ISO string`), while PostgreSQL transparently rewrites `@param` into positional `$1, $2, ...` while preserving string literals and comments.
- **Dialect-Normalized Reading (`DataRecordReader`):** Uniform typed getters (`isNull`, `getBoolean`, `getInt32`, `getInt64`, `getDouble`, `getDecimal`, `getString`, `getGuid`, `getDateTimeOffset`, `getBytes`) across SQLite primitives and PostgreSQL native data types.
- **Dual-Engine Providers:**
  - `DbProviderSqlite`: Embedded database engine via `better-sqlite3` with foreign key enforcement and automatic directory creation.
  - `DbProviderPostgres`: Connection-pooled client via `pg` with ADO.NET and URI connection string parsing.
- **Transactions & Concurrency Guards:**
  - `executeInTransaction<T>` for automatic commit/rollback.
  - Single-operation concurrency latching and failure latching (latched failed transactions reject further commands and prevent commits).
- **Wire-Compatible Migrations (`MigrationRunner` & `MigrationCatalog`):**
  - Universal and dialect-paired migration steps.
  - SHA-256 fingerprint verification identical to C# formula.
  - Transaction-serialized execution with PostgreSQL advisory lock `pg_try_advisory_xact_lock(564535375002)` and SQLite `BEGIN IMMEDIATE`.
- **Configuration Integration (`@nyssa-ai/configuration`):**
  - Stored under namespace `'nyssa.dbgateway'` with seamless `DbGatewayConfigurationStore` helpers.

## Installation

```bash
npm install @nyssa-ai/db-gateway
```

## Quick Start

### 1. Basic Ad-Hoc Execution

```typescript
import { DbGatewayClient, DbProviderSqlite } from "@nyssa-ai/db-gateway";

const factory = DbProviderSqlite.prepare({
  databaseProvider: "sqlite",
  connectionString: "Data Source=app.db",
  commandTimeoutSeconds: 30
});

const gateway = new DbGatewayClient(factory());

// Execute write
await gateway.execute(
  "INSERT INTO users (id, name, is_active) VALUES (@id, @name, @isActive)",
  { id: "u-1", name: "Alice", isActive: true }
);

// Query single record
const user = await gateway.querySingle<{ id: string; name: string }>(
  "SELECT id, name FROM users WHERE id = @id",
  { id: "u-1" }
);
```

### 2. Typed Commands

```typescript
import type { DbNonQuery, ParameterBinder, DbDialect } from "@nyssa-ai/db-gateway";

class InsertUserCommand implements DbNonQuery {
  constructor(private readonly id: string, private readonly email: string) {}

  getSql(dialect: DbDialect): string {
    return dialect === "sqlite"
      ? "INSERT INTO users (id, email) VALUES (@id, @email)"
      : "INSERT INTO users (id, email) VALUES (@id, @email) ON CONFLICT DO NOTHING";
  }

  bindParameters(binder: ParameterBinder): void {
    binder.add("id", this.id);
    binder.add("email", this.email);
  }
}

await gateway.execute(new InsertUserCommand("u-2", "bob@example.com"));
```

### 3. Transactions

```typescript
await gateway.executeInTransaction(async (tx) => {
  await tx.execute("INSERT INTO orders (id, total) VALUES (@id, @total)", { id: "o-1", total: 100 });
  await tx.execute("UPDATE accounts SET balance = balance - 100 WHERE id = @accountId", { accountId: "a-1" });
});
```

### 4. Running Migrations

```typescript
import { MigrationCatalog, MigrationStep, MigrationReleasePin, MigrationRunner } from "@nyssa-ai/db-gateway";

const catalog = MigrationCatalog.create(
  [
    MigrationStep.paired(
      1,
      "CREATE TABLE users (id TEXT PRIMARY KEY, active INTEGER NOT NULL);",
      "CREATE TABLE users (id UUID PRIMARY KEY, active BOOLEAN NOT NULL);"
    ),
    MigrationStep.universal(
      2,
      "ALTER TABLE users ADD COLUMN name TEXT;"
    )
  ],
  [
    new MigrationReleasePin("1.0.0", 1),
    new MigrationReleasePin("1.1.0", 2)
  ]
);

const runner = new MigrationRunner(factory(), catalog);
const report = await runner.migrate("1.1.0");
console.log(`Applied ${report.appliedCount} migrations, now at step ${report.toNumber}`);
```

## Testing

```bash
# Fast deterministic unit tests (no external services needed)
npm run test:unit

# Integration and use-case tests
npm run test:usecase
```
