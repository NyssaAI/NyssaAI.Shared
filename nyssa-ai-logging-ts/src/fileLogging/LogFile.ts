// Responsibility: Manage active log file, synchronous appending, size rotation, and retention pruning.
// Public interface:
// `-- LogFile
//     +-- BACKUP_RETENTION
//     +-- RETENTION_DAYS
//     +-- ROTATION_TRIGGER_MB
//     +-- dispose()
//     +-- initialize()
//     +-- new(logPath, rotationTriggerBytes, clock)
//     `-- writeRecord(record)

import * as fs from "node:fs";
import * as path from "node:path";
import type { Clock } from "./Clock.js";
import { SystemClock } from "./SystemClock.js";
import type { LogRecord } from "./LogRecord.js";
import { LogRecordSerializer } from "./LogRecordSerializer.js";
import { OwnerOnlyAccess } from "./OwnerOnlyAccess.js";
import { StderrDiagnostics } from "./StderrDiagnostics.js";

const BYTES_PER_MEGABYTE = 1024 * 1024;
const REOPEN_RETRY_DELAY_MS = 1000;

export class LogFile {
  public static readonly ROTATION_TRIGGER_MB = 10;
  public static readonly BACKUP_RETENTION = 5;
  public static readonly RETENTION_DAYS = 30;

  private readonly logPath: string;
  private readonly rotationTriggerBytes: number;
  private readonly clock: Clock;
  private fd: number | null = null;
  private bytesWritten = 0;
  private rotationRetryAtBytes = 0;
  private reopenRetryAtMs: number | null = null;
  private disposed = false;

  public constructor(
    logPath: string,
    rotationTriggerBytes?: number,
    clock?: Clock
  ) {
    this.logPath = logPath;
    this.rotationTriggerBytes =
      rotationTriggerBytes ?? LogFile.ROTATION_TRIGGER_MB * BYTES_PER_MEGABYTE;
    this.clock = clock ?? new SystemClock();
  }

  public initialize(): string | null {
    try {
      const directoryPath = path.dirname(this.logPath);
      if (!directoryPath || directoryPath === ".") {
        return `Invalid log path: ${this.logPath}`;
      }

      OwnerOnlyAccess.createDirectory(directoryPath);

      this.fd = this.openAppend(this.logPath);
      this.bytesWritten = fs.fstatSync(this.fd).size;

      OwnerOnlyAccess.applyToFile(this.logPath);
      this.deleteExpiredBackups();

      return null;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      return `Failed to initialize log file: ${message}`;
    }
  }

  public writeRecord(record: LogRecord): void {
    if (this.disposed) {
      return;
    }

    try {
      if (this.fd === null) {
        const nowMs = this.clock.now().getTime();
        if (this.reopenRetryAtMs !== null && nowMs < this.reopenRetryAtMs) {
          return;
        }

        this.tryReopenAfterFailedRotation();
        if (this.fd === null) {
          return;
        }
      }

      if (
        this.bytesWritten > this.rotationTriggerBytes &&
        this.bytesWritten >= this.rotationRetryAtBytes
      ) {
        this.rotate();
      }

      if (this.fd === null) {
        return;
      }

      const line = LogRecordSerializer.serialize(record);
      fs.writeSync(this.fd, line + "\n");
      fs.fsyncSync(this.fd);

      this.bytesWritten = fs.fstatSync(this.fd).size;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      StderrDiagnostics.error(this.logPath, `failed to write log record: ${message}`);
    }
  }

  public dispose(): void {
    if (this.disposed) {
      return;
    }
    this.disposed = true;
    if (this.fd !== null) {
      try {
        fs.closeSync(this.fd);
      } catch {
        // Disposed best-effort
      }
      this.fd = null;
    }
  }

  private openAppend(targetPath: string): number {
    return fs.openSync(targetPath, "a", OwnerOnlyAccess.OWNER_READ_WRITE);
  }

  private rotate(): void {
    try {
      if (this.fd !== null) {
        fs.closeSync(this.fd);
        this.fd = null;
      }

      for (let index = LogFile.BACKUP_RETENTION - 1; index >= 1; index--) {
        const olderPath = `${this.logPath}.${index}`;
        const newerPath = `${this.logPath}.${index + 1}`;

        if (!fs.existsSync(olderPath)) {
          continue;
        }

        if (fs.existsSync(newerPath)) {
          fs.unlinkSync(newerPath);
        }

        fs.renameSync(olderPath, newerPath);
      }

      const archivePath = `${this.logPath}.1`;
      if (fs.existsSync(archivePath)) {
        fs.unlinkSync(archivePath);
      }

      if (fs.existsSync(this.logPath)) {
        fs.renameSync(this.logPath, archivePath);
      }

      this.deleteExpiredBackups();

      this.fd = this.openAppend(this.logPath);
      OwnerOnlyAccess.applyToFile(this.logPath);
      this.bytesWritten = 0;
      this.rotationRetryAtBytes = 0;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      StderrDiagnostics.error(this.logPath, `failed to rotate log file: ${message}`);
      this.tryReopenAfterFailedRotation();
    }
  }

  private deleteExpiredBackups(): void {
    const directory = path.dirname(this.logPath);
    if (!directory || !fs.existsSync(directory)) {
      return;
    }

    const baseName = path.basename(this.logPath);
    const cutoffMs =
      this.clock.now().getTime() - LogFile.RETENTION_DAYS * 24 * 60 * 60 * 1000;

    for (let index = 1; index <= LogFile.BACKUP_RETENTION; index++) {
      const backupPath = path.join(directory, `${baseName}.${index}`);
      if (!fs.existsSync(backupPath)) {
        continue;
      }

      try {
        const stat = fs.statSync(backupPath);
        if (stat.mtimeMs < cutoffMs) {
          fs.unlinkSync(backupPath);
        }
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        StderrDiagnostics.warning(backupPath, `failed to prune expired backup: ${message}`);
      }
    }
  }

  private tryReopenAfterFailedRotation(): void {
    try {
      this.fd = this.openAppend(this.logPath);
      this.bytesWritten = fs.fstatSync(this.fd).size;
      this.rotationRetryAtBytes = this.bytesWritten + BYTES_PER_MEGABYTE;
      this.reopenRetryAtMs = null;
    } catch (error) {
      this.reopenRetryAtMs = this.clock.now().getTime() + REOPEN_RETRY_DELAY_MS;
      const message = error instanceof Error ? error.message : String(error);
      StderrDiagnostics.error(
        this.logPath,
        `failed to reopen log file after failed rotation: ${message}`
      );
    }
  }
}
