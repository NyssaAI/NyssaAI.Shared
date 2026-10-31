// Responsibility: Fail-safe diagnostic reporting to standard error for internal logging faults.
// Public interface:
// `-- StderrDiagnostics
//     +-- error(subject, detail)
//     +-- format(levelTag, subject, detail)
//     +-- warning(subject, detail)
//     `-- writeLine(line)

import * as os from "node:os";

export class StderrDiagnostics {
  public static error(subject: string, detail: string): void {
    this.write("erro", subject, detail);
  }

  public static warning(subject: string, detail: string): void {
    this.write("warn", subject, detail);
  }

  public static format(levelTag: string, subject: string, detail: string): string {
    return `${levelTag}: ${subject}${os.EOL}      ${detail}`;
  }

  public static writeLine(line: string): void {
    try {
      process.stderr.write(line + os.EOL);
    } catch {
      // Nowhere left to report to.
    }
  }

  private static write(levelTag: string, subject: string, detail: string): void {
    this.writeLine(this.format(levelTag, subject, detail));
  }
}
