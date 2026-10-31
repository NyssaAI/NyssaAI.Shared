import { describe, it, expect } from "vitest";
import * as os from "node:os";
import { StderrDiagnostics } from "../../src/fileLogging/StderrDiagnostics.js";
import { StderrCapture } from "./helpers/StderrCapture.js";

describe("StderrDiagnostics", () => {
  it("format_errorLevel_producesCanonicalFormat", () => {
    const formatted = StderrDiagnostics.format("erro", "application.log", "failed to open");
    expect(formatted).toBe(`erro: application.log${os.EOL}      failed to open`);
  });

  it("format_warningLevel_producesCanonicalFormat", () => {
    const formatted = StderrDiagnostics.format("warn", "backup.1", "expired file");
    expect(formatted).toBe(`warn: backup.1${os.EOL}      expired file`);
  });

  it("error_called_writesToStandardErrorWithoutThrowing", () => {
    const stderr = new StderrCapture();
    try {
      StderrDiagnostics.error("config.log", "access denied");
      expect(stderr.text).toContain("erro: config.log");
      expect(stderr.text).toContain("access denied");
    } finally {
      stderr.dispose();
    }
  });

  it("warning_called_writesToStandardErrorWithoutThrowing", () => {
    const stderr = new StderrCapture();
    try {
      StderrDiagnostics.warning("rotation", "slow I/O");
      expect(stderr.text).toContain("warn: rotation");
      expect(stderr.text).toContain("slow I/O");
    } finally {
      stderr.dispose();
    }
  });
});
