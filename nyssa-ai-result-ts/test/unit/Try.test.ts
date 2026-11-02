import { describe, expect, it } from "vitest";
import { Error } from "../../src/Error.js";
import { ErrorCategory } from "../../src/ErrorCategory.js";
import { try_, tryAsync } from "../../src/Result.js";

describe("try_ and tryAsync explicit exception boundaries", () => {
  it("try_actionSuccess_returnsOk", () => {
    // Arrange & Act
    const res = try_(() => Math.floor(100 / 2));

    // Assert
    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.value).toBe(50);
    }
  });

  it("try_actionThrows_returnsFailureWithCaughtError", () => {
    // Arrange & Act
    const res = try_((): number => {
      throw new RangeError("Division by zero");
    });

    // Assert
    expect(res.ok).toBe(false);
    expect(res.errors).toHaveLength(1);
    expect(res.errors[0]?.technicalMessage).toContain("RangeError: Division by zero");
    expect(res.errors[0]?.category).toBe(ErrorCategory.Unexpected);
  });

  it("try_withErrorHandler_translatesThrownException", () => {
    // Arrange & Act
    const res = try_(
      (): number => {
        throw new Error("Invalid input");
      },
      (ex) =>
        Error.fromMessages(
          "Invalid calculation input",
          `Math error: ${String(ex)}`,
          ErrorCategory.Validation
        )
    );

    // Assert
    expect(res.ok).toBe(false);
    expect(res.errors[0]?.userMessage).toBe("Invalid calculation input");
    expect(res.errors[0]?.category).toBe(ErrorCategory.Validation);
  });

  it("try_withErrorHandlerThrowing_preservesHandlerException", () => {
    // Arrange & Act
    const res = try_(
      (): number => {
        throw new globalThis.Error("Original failure");
      },
      (): Error => {
        throw new TypeError("Handler threw");
      }
    );

    // Assert
    expect(res.ok).toBe(false);
    expect(res.errors[0]?.technicalMessage).toContain("TypeError: Handler threw");
  });

  it("try_cancellationWithAbortedSignal_mapsToCanceledCategory", () => {
    // Arrange
    const controller = new AbortController();
    controller.abort();

    // Act
    const res = try_(
      (): number => {
        throw new DOMException("Aborted", "AbortError");
      },
      undefined,
      controller.signal
    );

    // Assert
    expect(res.ok).toBe(false);
    expect(res.errors[0]?.category).toBe(ErrorCategory.Canceled);
  });

  it("try_timeoutException_mapsToUnavailableCategory", () => {
    // Arrange & Act
    const res = try_((): number => {
      throw new DOMException("Socket timeout", "TimeoutError");
    });

    // Assert
    expect(res.ok).toBe(false);
    expect(res.errors[0]?.category).toBe(ErrorCategory.Unavailable);
    expect(res.errors[0]?.userMessage.toLowerCase()).toContain("timed out");
  });

  it("tryAsync_successAndRejection_translateCorrectly", async () => {
    // Arrange & Act
    const okRes = await tryAsync(async () => 42);
    const failRes = await tryAsync(async (): Promise<number> => {
      throw new globalThis.Error("Remote closed connection");
    });

    // Assert
    expect(okRes.ok).toBe(true);
    if (okRes.ok) {
      expect(okRes.value).toBe(42);
    }
    expect(failRes.ok).toBe(false);
    expect(failRes.errors[0]?.technicalMessage).toContain("Remote closed connection");
  });
});
