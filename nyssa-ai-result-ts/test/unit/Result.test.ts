import { describe, expect, it } from "vitest";
import { Error } from "../../src/Error.js";
import { ErrorCategory } from "../../src/ErrorCategory.js";
import { Result, fail, failError, ok } from "../../src/Result.js";
import { ResultException } from "../../src/ResultException.js";

describe("Result core state and pure combinators", () => {
  it("result_ok_holdsValueAndEmptyErrors", () => {
    // Arrange & Act
    const res = ok(42);

    // Assert
    expect(res.ok).toBe(true);
    expect(res.isSuccess).toBe(true);
    expect(res.isFailure).toBe(false);
    expect(res.value).toBe(42);
    expect(res.errors).toEqual([]);
    expect(res.error).toBeUndefined();
    expect(res.message).toBe("");
  });

  it("result_failWithError_holdsStructuredError", () => {
    // Arrange
    const err = Error.fromMessage("Not found", ErrorCategory.NotFound);

    // Act
    const res: Result<number> = fail(err);

    // Assert
    expect(res.ok).toBe(false);
    expect(res.isSuccess).toBe(false);
    expect(res.isFailure).toBe(true);
    expect(res.errors).toEqual([err]);
    expect(res.category).toBe(ErrorCategory.NotFound);
    expect(res.message).toBe("Not found");
    if (!res.ok) {
      expect(res.error).toBe(err);
    }
  });

  it("result_failWithString_constructsValidationError", () => {
    // Arrange & Act
    const res: Result<string> = fail("Invalid payload");

    // Assert
    expect(res.ok).toBe(false);
    expect(res.isFailure).toBe(true);
    expect(res.errors).toHaveLength(1);
    expect(res.errors[0]?.userMessage).toBe("Invalid payload");
    expect(res.category).toBe(ErrorCategory.Validation);
  });

  it("result_failError_wrapsCustomDomainError", () => {
    // Arrange & Act
    const res: Result<number, "INVALID_COUPON"> = failError("INVALID_COUPON");

    // Assert
    expect(res.ok).toBe(false);
    if (!res.ok) {
      expect(res.error).toBe("INVALID_COUPON");
      expect(res.message).toBe("INVALID_COUPON");
    }
  });

  it("result_throwIfErrorAndUnwrap_returnValueOrThrowResultException", () => {
    // Arrange
    const success = ok("valid");
    const failure: Result<string> = fail("failed operation");

    // Act & Assert
    expect(success.throwIfError()).toBe("valid");
    expect(success.unwrap()).toBe("valid");

    expect(() => failure.throwIfError()).toThrow(ResultException);
    expect(() => failure.unwrap()).toThrow("failed operation");
  });

  it("result_tryGetValue_returnsDiscriminatedTuple", () => {
    // Arrange
    const success = ok("data");
    const failure: Result<string> = fail("error");

    // Act
    const [okFlag, okVal] = success.tryGetValue();
    const [failFlag, failVal] = failure.tryGetValue();

    // Assert
    expect(okFlag).toBe(true);
    expect(okVal).toBe("data");
    expect(failFlag).toBe(false);
    expect(failVal).toBeUndefined();
  });

  it("result_thenAndTransform_chainOnSuccessAndShortCircuitOnFailure", () => {
    // Arrange
    let calledOnFailure = false;
    const initialOk: Result<number> = ok(10);
    const initialFail: Result<number> = fail("initial error");

    // Act
    const chainedOk = initialOk
      .then((v) => ok(v * 2))
      .transform((v) => `Result: ${v}`);
    const chainedFail = initialFail
      .then((v) => {
        calledOnFailure = true;
        return ok(v * 2);
      })
      .transform((v) => `Result: ${v}`);

    // Assert
    expect(chainedOk.ok).toBe(true);
    if (chainedOk.ok) {
      expect(chainedOk.value).toBe("Result: 20");
    }
    expect(chainedFail.ok).toBe(false);
    expect(calledOnFailure).toBe(false);
  });

  it("result_pureCombinators_doNotCatchThrownExceptions", () => {
    // Arrange
    const success = ok(5);

    // Act & Assert
    expect(() =>
      success.then(() => {
        throw new ReferenceError("Defect in then callback");
      })
    ).toThrow(ReferenceError);

    expect(() =>
      success.transform(() => {
        throw new TypeError("Defect in transform callback");
      })
    ).toThrow(TypeError);
  });

  it("result_resolveAndResolveAsync_executeMatchingBranch", async () => {
    // Arrange
    const success = ok(10);
    const failure: Result<number> = fail("boom");

    // Act
    const syncOk = success.resolve(
      (v) => `Value: ${v}`,
      (errs) => `Error: ${errs[0]?.userMessage}`
    );
    const syncFail = failure.resolve(
      (v) => `Value: ${v}`,
      (errs) => `Error: ${errs[0]?.userMessage}`
    );
    const asyncOk = await success.resolveAsync(
      async (v) => `Async: ${v}`,
      async (errs) => `Async error: ${errs[0]?.userMessage}`
    );
    const asyncFail = await failure.resolveAsync(
      async (v) => `Async: ${v}`,
      async (errs) => `Async error: ${errs[0]?.userMessage}`
    );

    // Assert
    expect(syncOk).toBe("Value: 10");
    expect(syncFail).toBe("Error: boom");
    expect(asyncOk).toBe("Async: 10");
    expect(asyncFail).toBe("Async error: boom");
  });
});
