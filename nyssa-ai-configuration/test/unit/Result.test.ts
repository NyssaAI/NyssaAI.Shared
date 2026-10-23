import { describe, it, expect } from "vitest";
import { ok, fail } from "../../src/results/Result.js";

describe("Result", () => {
  it("result_success_containsValue", () => {
    // Arrange & Act
    const res = ok(123);

    // Assert
    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.value).toBe(123);
    }
  });

  it("result_failure_containsError", () => {
    // Arrange & Act
    const res = fail("operation failed");

    // Assert
    expect(res.ok).toBe(false);
    if (!res.ok) {
      expect(res.error).toBe("operation failed");
    }
  });
});
