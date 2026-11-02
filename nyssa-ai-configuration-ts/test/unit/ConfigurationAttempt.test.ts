import { describe, it, expect } from "vitest";
import { ok, fail } from "@nyssa-ai/result";
import { ConfigurationAttempt } from "../../src/values/ConfigurationAttempt.js";

describe("ConfigurationAttempt", () => {
  it("attempt_success_containsValue", () => {
    // Arrange & Act
    const attempt = new ConfigurationAttempt(ok(123));

    // Assert
    expect(attempt.ok).toBe(true);
    expect(attempt.isSuccess).toBe(true);
    expect(attempt.isFailure).toBe(false);
    expect(attempt.value).toBe(123);
  });

  it("attempt_failure_containsError", () => {
    // Arrange & Act
    const attempt = new ConfigurationAttempt(fail("operation failed"));

    // Assert
    expect(attempt.ok).toBe(false);
    expect(attempt.isSuccess).toBe(false);
    expect(attempt.isFailure).toBe(true);
    expect(attempt.error).toBe("operation failed");
  });
});
