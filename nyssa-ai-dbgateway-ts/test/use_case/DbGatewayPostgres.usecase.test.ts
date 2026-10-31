// Responsibility: Use-case test PostgreSQL execution, transactions, parameter rewriting, and advisory locking.
// Public interface:
// (none)

import { describe, it, expect, beforeAll, afterAll } from "vitest";
import { DbGatewayClient } from "../../src/gateway/DbGatewayClient.js";
import { DbProviderPostgres } from "../../src/providers/DbProviderPostgres.js";

const postgresConnString =
  process.env["NYSSAAI_DBGATEWAY_POSTGRES"] ??
  process.env["NYSSAAI_REPOSITORY_POSTGRES"];

const runPostgres = postgresConnString ? describe : describe.skip;

runPostgres("DbGatewayPostgres Use Cases", () => {
  let gateway: DbGatewayClient;

  beforeAll(async () => {
    const factory = DbProviderPostgres.prepare({
      databaseProvider: "postgresql",
      connectionString: postgresConnString!,
      commandTimeoutSeconds: 15
    });
    gateway = new DbGatewayClient(factory(), 15);

    await gateway.execute(`
      CREATE TABLE IF NOT EXISTS pg_uc_tasks (
        id UUID PRIMARY KEY,
        title TEXT NOT NULL,
        is_completed BOOLEAN NOT NULL,
        created_at TIMESTAMPTZ NOT NULL
      );
    `);
    await gateway.execute("DELETE FROM pg_uc_tasks;");
  });

  afterAll(async () => {
    if (gateway) {
      await gateway.execute("DROP TABLE IF EXISTS pg_uc_tasks;");
    }
  });

  it("postgresGateway_adHocExecution_insertsAndQueriesWithParameterRewriting", async () => {
    const id = "a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11";
    const date = new Date("2026-10-31T12:00:00.000Z");

    const inserted = await gateway.execute(
      `INSERT INTO pg_uc_tasks (id, title, is_completed, created_at)
       VALUES (@id, @title, @isCompleted, @createdAt)`,
      {
        id,
        title: "Postgres Task 1",
        isCompleted: true,
        createdAt: date
      }
    );
    expect(inserted).toBe(1);

    const count = await gateway.scalar<number>("SELECT COUNT(*) FROM pg_uc_tasks;");
    expect(Number(count)).toBe(1);

    const single = await gateway.querySingle<{ id: string; title: string; is_completed: boolean }>(
      "SELECT id, title, is_completed FROM pg_uc_tasks WHERE id = @id",
      { id }
    );
    expect(single?.title).toBe("Postgres Task 1");
    expect(single?.is_completed).toBe(true);
  });

  it("postgresGateway_transactions_commitsAndRollsBack", async () => {
    const id1 = "b0eebc99-9c0b-4ef8-bb6d-6bb9bd380a22";
    const id2 = "c0eebc99-9c0b-4ef8-bb6d-6bb9bd380a33";
    const date = new Date();

    // Commit
    await gateway.executeInTransaction(async (tx) => {
      await tx.execute(
        `INSERT INTO pg_uc_tasks (id, title, is_completed, created_at) VALUES (@id, @title, @isCompleted, @createdAt)`,
        { id: id1, title: "Tx Task 1", isCompleted: false, createdAt: date }
      );
    });

    const exists1 = await gateway.scalar<number>("SELECT COUNT(*) FROM pg_uc_tasks WHERE id = @id", { id: id1 });
    expect(Number(exists1)).toBe(1);

    // Rollback
    await expect(
      gateway.executeInTransaction(async (tx) => {
        await tx.execute(
          `INSERT INTO pg_uc_tasks (id, title, is_completed, created_at) VALUES (@id, @title, @isCompleted, @createdAt)`,
          { id: id2, title: "Tx Task 2", isCompleted: false, createdAt: date }
        );
        throw new Error("Force rollback");
      })
    ).rejects.toThrow("Force rollback");

    const exists2 = await gateway.scalar<number>("SELECT COUNT(*) FROM pg_uc_tasks WHERE id = @id", { id: id2 });
    expect(Number(exists2)).toBe(0);
  });
});
