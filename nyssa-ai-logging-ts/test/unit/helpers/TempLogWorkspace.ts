import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import * as crypto from "node:crypto";
import type { LogRecord } from "../../src/fileLogging/LogRecord.js";

export class TempLogWorkspace {
  public readonly directoryPath: string;
  public readonly logPath: string;

  public constructor(fileName = "application.log") {
    const unique = crypto.randomBytes(8).toString("hex");
    this.directoryPath = path.join(os.tmpdir(), `NyssaLoggingTest_${unique}`);
    fs.mkdirSync(this.directoryPath, { recursive: true });
    this.logPath = path.join(this.directoryPath, fileName);
  }

  public readRecords(filePath?: string): LogRecord[] {
    const target = filePath ?? this.logPath;
    if (!fs.existsSync(target)) {
      return [];
    }
    const content = fs.readFileSync(target, "utf8");
    return content
      .split("\n")
      .map(line => line.trim())
      .filter(line => line.length > 0)
      .map(line => JSON.parse(line) as LogRecord);
  }

  public dispose(): void {
    if (fs.existsSync(this.directoryPath)) {
      try {
        fs.rmSync(this.directoryPath, { recursive: true, force: true });
      } catch {
        // Best-effort cleanup
      }
    }
  }
}
