// Responsibility: Manage write-ahead journals (.pending/<ns>.journal.json and .pending/<ns>.move.json) for crash-resilient multi-step writes.
// Public interface:
// `-- PendingJournalManager
//     +-- computeDocumentSha256(documentPath)
//     +-- computeStringSha256(content)
//     +-- createMoveJournal(namespace, key, sourceProvider, sourceSecretName)
//     +-- createWriteJournal(namespace, expectedExistingSha256, plannedDocument, vaultWrites)
//     +-- deleteMoveJournal(namespace)
//     +-- deleteWriteJournal(namespace)
//     +-- hasMoveJournal(namespace)
//     +-- hasWriteJournal(namespace)
//     +-- moveJournalPath(namespace)
//     +-- new(configurationDirectory)
//     +-- readMoveJournal(namespace)
//     +-- readWriteJournal(namespace)
//     +-- updateWriteJournalStage(namespace, stage)
//     `-- writeJournalPath(namespace)

import * as fs from "node:fs";
import * as path from "node:path";
import * as crypto from "node:crypto";
import { ok, fail, type Result } from "../results/Result.js";
import { ProtectedFileSystem } from "../fileSecurity/ProtectedFileSystem.js";
import { ConfigurationWriteRecoveryHandle } from "./ConfigurationWriteRecoveryHandle.js";
import type { ConfigurationValue } from "../values/ConfigurationValue.js";

/**
 * PendingJournalManager persists and inspects write-ahead journals in <configDir>/.pending/.
 */
export class PendingJournalManager {
  private readonly _pendingDirectory: string;

  public constructor(configurationDirectory: string) {
    this._pendingDirectory = path.join(path.resolve(configurationDirectory), ".pending");
  }

  public writeJournalPath(namespace: string): string {
    return path.join(this._pendingDirectory, `${namespace}.journal.json`);
  }

  public moveJournalPath(namespace: string): string {
    return path.join(this._pendingDirectory, `${namespace}.move.json`);
  }

  public hasWriteJournal(namespace: string): boolean {
    return fs.existsSync(this.writeJournalPath(namespace));
  }

  public hasMoveJournal(namespace: string): boolean {
    return fs.existsSync(this.moveJournalPath(namespace));
  }

  public static computeStringSha256(content: string): string {
    return crypto.createHash("sha256").update(content, "utf8").digest("hex");
  }

  public static computeDocumentSha256(documentPath: string): string {
    if (!fs.existsSync(documentPath)) {
      return this.computeStringSha256("{}");
    }
    try {
      const raw = fs.readFileSync(documentPath, "utf8");
      if (!raw || raw.trim().length === 0) {
        return this.computeStringSha256("{}");
      }
      const parsed = JSON.parse(raw) as Record<string, ConfigurationValue>;
      return this.computeStringSha256(JSON.stringify(parsed, null, 2));
    } catch {
      const raw = fs.readFileSync(documentPath, "utf8");
      return this.computeStringSha256(raw);
    }
  }

  public createWriteJournal(
    namespace: string,
    expectedExistingSha256: string,
    plannedDocument: Record<string, ConfigurationValue>,
    vaultWrites: ReadonlyArray<{
      readonly provider: string;
      readonly secretName: string;
      readonly value: string;
    }>
  ): Result<ConfigurationWriteRecoveryHandle> {
    const operationId = crypto.randomUUID();
    const plannedJson = JSON.stringify(plannedDocument, null, 2);
    const plannedNewSha256 = PendingJournalManager.computeStringSha256(plannedJson);
    const journalPath = this.writeJournalPath(namespace);

    const record = {
      operationId,
      namespace,
      stage: "vault" as const,
      expectedExistingSha256,
      plannedNewSha256,
      vaultWrites,
      plannedDocument
    };

    const writeRes = ProtectedFileSystem.createFile(journalPath, JSON.stringify(record, null, 2));
    if (!writeRes.ok) {
      return writeRes;
    }

    return ok(new ConfigurationWriteRecoveryHandle(namespace, operationId, journalPath));
  }

  public updateWriteJournalStage(namespace: string, stage: "vault" | "local"): Result<void> {
    const readRes = this.readWriteJournal(namespace);
    if (!readRes.ok || !readRes.value) {
      return fail(`No pending write journal found for namespace '${namespace}'.`, "NotFound");
    }
    const updated = {
      ...readRes.value,
      stage
    };
    const writeRes = ProtectedFileSystem.createFile(
      this.writeJournalPath(namespace),
      JSON.stringify(updated, null, 2)
    );
    if (!writeRes.ok) {
      return writeRes;
    }
    return ok(undefined);
  }

  public readWriteJournal(namespace: string): Result<{
    readonly operationId: string;
    readonly namespace: string;
    readonly stage: "vault" | "local";
    readonly expectedExistingSha256: string;
    readonly plannedNewSha256: string;
    readonly vaultWrites: ReadonlyArray<{
      readonly provider: string;
      readonly secretName: string;
      readonly value: string;
    }>;
    readonly plannedDocument: Record<string, ConfigurationValue>;
  } | null> {
    const journalPath = this.writeJournalPath(namespace);
    if (!fs.existsSync(journalPath)) {
      return ok(null);
    }
    try {
      const raw = fs.readFileSync(journalPath, "utf8");
      const parsed = JSON.parse(raw) as {
        readonly operationId: string;
        readonly namespace: string;
        readonly stage: "vault" | "local";
        readonly expectedExistingSha256: string;
        readonly plannedNewSha256: string;
        readonly vaultWrites: ReadonlyArray<{
          readonly provider: string;
          readonly secretName: string;
          readonly value: string;
        }>;
        readonly plannedDocument: Record<string, ConfigurationValue>;
      };
      return ok(parsed);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to read write journal '${journalPath}': ${message}`, "Validation");
    }
  }

  public deleteWriteJournal(namespace: string): void {
    const journalPath = this.writeJournalPath(namespace);
    try {
      if (fs.existsSync(journalPath)) {
        fs.unlinkSync(journalPath);
      }
    } catch {
      // ignore
    }
  }

  public createMoveJournal(
    namespace: string,
    key: string,
    sourceProvider: string,
    sourceSecretName: string
  ): Result<void> {
    const journalPath = this.moveJournalPath(namespace);
    const record = {
      operationId: crypto.randomUUID(),
      namespace,
      key,
      sourceProvider,
      sourceSecretName
    };
    const writeRes = ProtectedFileSystem.createFile(journalPath, JSON.stringify(record, null, 2));
    if (!writeRes.ok) {
      return writeRes;
    }
    return ok(undefined);
  }

  public readMoveJournal(namespace: string): Result<{
    readonly operationId: string;
    readonly namespace: string;
    readonly key: string;
    readonly sourceProvider: string;
    readonly sourceSecretName: string;
  } | null> {
    const journalPath = this.moveJournalPath(namespace);
    if (!fs.existsSync(journalPath)) {
      return ok(null);
    }
    try {
      const raw = fs.readFileSync(journalPath, "utf8");
      const parsed = JSON.parse(raw) as {
        readonly operationId: string;
        readonly namespace: string;
        readonly key: string;
        readonly sourceProvider: string;
        readonly sourceSecretName: string;
      };
      return ok(parsed);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to read move journal '${journalPath}': ${message}`, "Validation");
    }
  }

  public deleteMoveJournal(namespace: string): void {
    const journalPath = this.moveJournalPath(namespace);
    try {
      if (fs.existsSync(journalPath)) {
        fs.unlinkSync(journalPath);
      }
    } catch {
      // ignore
    }
  }
}
