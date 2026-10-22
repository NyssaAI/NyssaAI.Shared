// Responsibility: Parse .NET TimeSpan strings (hh:mm:ss[.fff]) into milliseconds per TS-DATA.
// Public interface:
// `-- TimeSpanParser
//     +-- format(ms)
//     `-- parse(value)

import { ok, fail, type Result } from "../results/Result.js";

/**
 * TimeSpanParser validates and parses .NET TimeSpan string representations.
 */
export class TimeSpanParser {
  private static readonly TIME_SPAN_REGEX = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?$/;

  /**
   * Parses a TimeSpan string (e.g. "00:00:10" or "01:30:00.500") into positive milliseconds.
   */
  public static parse(value: string): Result<number> {
    if (!value || typeof value !== "string") {
      return fail("TimeSpan value must be a non-empty string.", "Validation");
    }

    const match = this.TIME_SPAN_REGEX.exec(value.trim());
    if (!match) {
      return fail(`Invalid TimeSpan format: '${value}'. Expected format hh:mm:ss[.fff]`, "Validation");
    }

    const days = match[1] ? parseInt(match[1], 10) : 0;
    const hours = parseInt(match[2] ?? "0", 10);
    const minutes = parseInt(match[3] ?? "0", 10);
    const seconds = parseInt(match[4] ?? "0", 10);

    if (hours > 23 || minutes > 59 || seconds > 59) {
      return fail(`Invalid TimeSpan component out of range: '${value}'.`, "Validation");
    }

    let fractionMs = 0;
    if (match[5]) {
      const fractionStr = match[5].padEnd(3, "0").slice(0, 3);
      fractionMs = parseInt(fractionStr, 10);
    }

    const totalMs =
      days * 86400000 +
      hours * 3600000 +
      minutes * 60000 +
      seconds * 1000 +
      fractionMs;

    if (totalMs <= 0) {
      return fail(
        `manifest: lockTimeout '${value}' is not a positive TimeSpan.`,
        "Validation"
      );
    }

    return ok(totalMs);
  }

  /**
   * Formats milliseconds into standard .NET TimeSpan string hh:mm:ss.
   */
  public static format(ms: number): string {
    const totalSeconds = Math.floor(ms / 1000);
    const seconds = totalSeconds % 60;
    const totalMinutes = Math.floor(totalSeconds / 60);
    const minutes = totalMinutes % 60;
    const hours = Math.floor(totalMinutes / 60);

    const pad = (n: number) => n.toString().padStart(2, "0");
    return `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
  }
}
