import { describe, expect, it } from "vitest";
import {
  CANCELED_USER_MESSAGE,
  DEFAULT_USER_MESSAGE,
  Error,
  TIMED_OUT_USER_MESSAGE,
} from "../../src/Error.js";
import { ErrorCategory } from "../../src/ErrorCategory.js";
import { ResultException } from "../../src/ResultException.js";

describe("Error and ErrorCategory", () => {
  it("error_fromMessage_setsUserAndTechnicalMessages", () => {
    // Arrange & Act
    const error = Error.fromMessage("Invalid configuration", ErrorCategory.Validation);

    // Assert
    expect(error.userMessage).toBe("Invalid configuration");
    expect(error.technicalMessage).toBe("Invalid configuration");
    expect(error.category).toBe(ErrorCategory.Validation);
    expect(error.stackTrace).toBeNull();
    expect(error.hasStackTrace).toBe(false);
    expect(error.causeType).toBeNull();
    expect(error.message).toBe("Invalid configuration");
    expect(Object.isFrozen(error)).toBe(true);
  });

  it("error_fromMessageWithNull_usesDefaultUserMessage", () => {
    // Arrange & Act
    const error = Error.fromMessage(null);

    // Assert
    expect(error.userMessage).toBe(DEFAULT_USER_MESSAGE);
    expect(error.technicalMessage).toBe(DEFAULT_USER_MESSAGE);
    expect(error.category).toBe(ErrorCategory.Unexpected);
  });

  it("error_fromMessages_preservesDistinctUserAndTechnicalMessages", () => {
    // Arrange & Act
    const error = Error.fromMessages(
      "Service unavailable",
      "Connection timeout connecting to 10.0.0.1:5432",
      ErrorCategory.Unavailable,
      "SocketTimeoutError"
    );

    // Assert
    expect(error.userMessage).toBe("Service unavailable");
    expect(error.technicalMessage).toBe("Connection timeout connecting to 10.0.0.1:5432");
    expect(error.category).toBe(ErrorCategory.Unavailable);
    expect(error.causeType).toBe("SocketTimeoutError");
    expect(error.message).toBe("Connection timeout connecting to 10.0.0.1:5432");
  });

  it("error_fromException_capturesCausalChainAndStackTraceWithoutRetainingException", () => {
    // Arrange
    const inner = new TypeError("Root cause invalid type");
    const outer = new RangeError("Higher level failure", { cause: inner });

    // Act
    const error = Error.fromException(outer, "Something went wrong", ErrorCategory.Unexpected);

    // Assert
    expect(error.userMessage).toBe("Something went wrong");
    expect(error.technicalMessage).toContain("RangeError: Higher level failure");
    expect(error.technicalMessage).toContain("TypeError: Root cause invalid type");
    expect(error.technicalMessage).toContain(" ---> ");
    expect(error.stackTrace).not.toBeNull();
    expect(error.hasStackTrace).toBe(true);
    expect(error.causeType).toBe("RangeError");
    expect("cause" in error).toBe(false);
  });

  it("error_fromExceptionWithNull_createsSentinelError", () => {
    // Arrange & Act
    const error = Error.fromException(null);

    // Assert
    expect(error.userMessage).toBe(DEFAULT_USER_MESSAGE);
    expect(error.technicalMessage).toContain("without any exception detail");
    expect(error.stackTrace).toBeNull();
    expect(error.hasStackTrace).toBe(false);
  });

  it("error_fromExceptions_flattensAggregateError", () => {
    // Arrange
    const aggregate = new AggregateError(
      [new TypeError("Err 1"), new RangeError("Err 2")],
      "Batch failed"
    );

    // Act
    const errors = Error.fromExceptions(aggregate, "Batch failed");

    // Assert
    expect(errors).toHaveLength(2);
    expect(errors[0]?.technicalMessage).toContain("TypeError: Err 1");
    expect(errors[1]?.technicalMessage).toContain("RangeError: Err 2");
  });

  it("error_fromExceptions_unwrapsResultException", () => {
    // Arrange
    const originalErrors = [
      Error.fromMessage("First failure", ErrorCategory.Validation),
      Error.fromMessage("Second failure", ErrorCategory.Conflict),
    ];
    const resExc = new ResultException(originalErrors);

    // Act
    const unwrapped = Error.fromExceptions(resExc);

    // Assert
    expect(unwrapped).toEqual(originalErrors);
    expect(resExc).toBeInstanceOf(globalThis.Error);
    expect(resExc).toBeInstanceOf(ResultException);
  });

  it("error_fromCancellation_classifiesByCallerEvidence", () => {
    // Arrange
    const abortController = new AbortController();
    abortController.abort();
    const abortError = new DOMException("The operation was aborted", "AbortError");
    const timeoutError = new DOMException("The operation timed out", "TimeoutError");

    // Act
    const canceledErr = Error.fromCancellation(abortError, abortController.signal);
    const timedOutErr = Error.fromCancellation(timeoutError);

    // Assert
    expect(canceledErr.category).toBe(ErrorCategory.Canceled);
    expect(canceledErr.userMessage).toBe(CANCELED_USER_MESSAGE);
    expect(timedOutErr.category).toBe(ErrorCategory.Unavailable);
    expect(timedOutErr.userMessage).toBe(TIMED_OUT_USER_MESSAGE);
  });

  it("error_atLeastOne_replacesEmptyOrNullWithSentinel", () => {
    // Arrange
    const validErr = Error.fromMessage("Valid error");

    // Act
    const sentinelEmpty = Error.atLeastOne([]);
    const sentinelNull = Error.atLeastOne(null);
    const filtered = Error.atLeastOne([null, validErr, undefined]);

    // Assert
    expect(sentinelEmpty).toHaveLength(1);
    expect(sentinelEmpty[0]?.userMessage).toBe(DEFAULT_USER_MESSAGE);
    expect(sentinelEmpty[0]?.technicalMessage).toContain("without any error");
    expect(sentinelNull).toHaveLength(1);
    expect(sentinelNull[0]?.technicalMessage).toContain("without any error");
    expect(filtered).toEqual([validErr]);
  });
});
