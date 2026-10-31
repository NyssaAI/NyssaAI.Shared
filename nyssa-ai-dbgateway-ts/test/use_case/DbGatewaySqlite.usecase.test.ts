// Responsibility: Use-case test SQLite execution, transactions, typed commands, and concurrency latching.
// Public interface:
// (none)

import { describe, it, expect, beforeEach, afterEach } from "vitest";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import { DbGatewayClient } from "../../src/gateway/DbGatewayClient.js";
import { DbProviderSqlite } from "../../src/providers/DbProviderSqlite.js";
import type { DbNonQuery } from "../../src/commands/DbNonQuery.js";
import type { DbCommand } from "../../src/commands/DbCommand.js";
import type { DbQuery } from "../../src/commands/DbQuery.js";
import type { ParameterBinder } from "../../src/commands/ParameterBinder.js";
import type { DataRecordReader } from "../../src/commands/DataRecordReader.js";

const createTask = (id: string, title: string, isCompleted: boolean): DbNonQuery => ({
  getSql: () => "INSERT INTO tasks (id, title, is_completed) VALUES (@id, @title, @isCompleted)",
  bindParameters: (binder: ParameterBinder) => {
    binder.add("id", id);
    binder.add("title", title);
    binder.add("isCompleted", isCompleted);
  }
});

const getTaskCount: DbCommand<number> = {
  getSql: () => "SELECT COUNT(*) FROM tasks",
  bindParameters: () => {},
  readResult: async (reader: DataRecordReader) => {
    if (await reader.next()) {
      return reader.getInt32(0);
    }
    return 0;
  }
};

const listTasks: DbQuery<{ readonly id: string; readonly title: string; readonly isCompleted: boolean }> = {
  getSql: () => "SELECT id, title, is_completed FROM tasks ORDER BY id",
  bindParameters: () => {},
  mapRow: (reader: DataRecordReader) => ({
    id: reader.getString("id"),
    title: reader.getString("title"),
    isCompleted: reader.getBoolean("is_completed")
  })
};

describe("DbGatewaySqlite Use Cases", () => {
  let tempDir: string;
  let dbPath: string;
  let gateway: DbGatewayClient;

  beforeEach(async () => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-sqlite-uc-"));
    dbPath = path.join(tempDir, "test.db");
    const factory = DbProviderSqlite.prepare({
      databaseProvider: "sqlite",
      connectionString: `Data Source=${dbPath}`,
      commandTimeoutSeconds: 10
    });
    gateway = new DbGatewayClient(factory(), 10);

    await gateway.execute(`
      CREATE TABLE tasks (
        id TEXT PRIMARY KEY,
        title TEXT NOT NULL,
        is_completed INTEGER NOT NULL
      );
    `);
  });

  afterEach(async () => {
    if (fs.existsSync(tempDir)) {
      fs.rmSync(tempDir, { recursive: true, force: true });
    }
  });

  it("sqliteGateway_adHocExecution_insertsAndQueriesRows", async () => {
    const inserted = await gateway.execute(
      "INSERT INTO tasks (id, title, is_completed) VALUES (@id, @title, @is_completed)",
      { id: "task-1", title: "First Task", is_completed: true }
    );
    expect(inserted).toBe(1);

    const count = await gateway.scalar<number>("SELECT COUNT(*) FROM tasks");
    expect(count).toBe(1);

    const single = await gateway.querySingle<{ id: string; title: string }>(
      "SELECT id, title FROM tasks WHERE id = @id",
      { id: "task-1" }
    );
    expect(single).toEqual({ id: "task-1", title: "First Task" });

    const all = await gateway.query<{ id: string; title: string }>("SELECT id, title FROM tasks");
    expect(all.length).toBe(1);
    expect(all[0]?.title).toBe("First Task");
  });

  it("sqliteGateway_typedCommands_executesTypedWorkflow", async () => {
    await gateway.execute(createTask("task-1", "Learn TypeScript", false));
    await gateway.execute(createTask("task-2", "Implement Gateway", true));

    const count = await gateway.executeCommand(getTaskCount);
    expect(count).toBe(2);

    const tasks = await gateway.query(listTasks);
    expect(tasks.length).toBe(2);
    expect(tasks[0]).toEqual({ id: "task-1", title: "Learn TypeScript", isCompleted: false });
    expect(tasks[1]).toEqual({ id: "task-2", title: "Implement Gateway", isCompleted: true });
  });

  it("sqliteGateway_executeInTransaction_commitsOnSuccessAndRollsBackOnError", async () => {
    // 1. Success case commits
    await gateway.executeInTransaction(async (tx) => {
      await tx.execute("INSERT INTO tasks (id, title, is_completed) VALUES (@id, @title, @isCompleted)", {
        id: "tx-task-1",
        title: "Tx Task 1",
        isCompleted: false
      });
      await tx.execute("INSERT INTO tasks (id, title, is_completed) VALUES (@id, @title, @isCompleted)", {
        id: "tx-task-2",
        title: "Tx Task 2",
        isCompleted: true
      });
    });

    const countAfterCommit = await gateway.scalar<number>("SELECT COUNT(*) FROM tasks");
    expect(countAfterCommit).toBe(2);

    // 2. Failure case rolls back
    await expect(
      gateway.executeInTransaction(async (tx) => {
        await tx.execute("INSERT INTO tasks (id, title, is_completed) VALUES (@id, @title, @isCompleted)", {
          id: "tx-task-3",
          title: "Tx Task 3",
          isCompleted: false
        });
        throw new Error("Simulated transactional failure!");
      })
    ).rejects.toThrow("Simulated transactional failure!");

    const countAfterRollback = await gateway.scalar<number>("SELECT COUNT(*) FROM tasks");
    expect(countAfterRollback).toBe(2);
  });

  it("sqliteGateway_transactionScope_enforcesSingleOperationAndFailureLatch", async () => {
    const tx = await gateway.beginTransaction();
    try {
      // Intentional failure latches the transaction
      await expect(tx.execute("INSERT INTO non_existent_table VALUES (1)")).rejects.toThrow();

      // Subsequent command must fail because transaction is latched as failed
      await expect(
        tx.execute("INSERT INTO tasks (id, title, is_completed) VALUES (@id, @title, 0)", {
          id: "latch-1",
          title: "Should Fail"
        })
      ).rejects.toThrow("The transaction failed and must be rolled back.");

      // Commit must fail
      await expect(tx.commit()).rejects.toThrow("The transaction failed and must be rolled back.");
    } finally {
      await tx[Symbol.asyncDispose]();
    }
  });
});
