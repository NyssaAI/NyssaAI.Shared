# nyssa-ai-dbgateway (Python)

Zero-ceremony, typed database gateway across SQLite and PostgreSQL matching `NyssaAI.DbGateway` (.NET) and `@nyssa-ai/db-gateway` (TypeScript).

## Features

- **Zero-Ceremony Gateway**: Single `DbGateway` interface (`execute`, `scalar`, `query`, `query_single`).
- **Engine-Agnostic Coercion**: Write `@param` placeholders in SQL across SQLite and PostgreSQL.
- **Typed Commands & Queries**: First-class `DbCommand[TResult]`, `DbNonQuery`, and `DbQuery[TRow]`.
- **Automatic Transactions**: `client.begin_transaction()` and `client.execute_transaction(lambda tx: ...)`.
- **Paired & Universal Migrations**: Dual-engine schema migrations with SHA-256 fingerprint verification and PostgreSQL session advisory locks.
