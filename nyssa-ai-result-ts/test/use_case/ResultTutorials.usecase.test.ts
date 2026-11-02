import { describe, expect, it } from "vitest";
import type { Result } from "../../src/Result.js";
import {
  Error,
  ErrorCategory,
  Guard,
  fail,
  ok,
  try_,
} from "../../src/index.js";

interface UserRegistrationRequest {
  readonly username: string;
  readonly email: string;
  readonly age: number;
}

interface RegisteredUser {
  readonly userId: number;
  readonly username: string;
  readonly email: string;
}

describe("Result use-case tutorials", () => {
  it("tutorial_userRegistrationPipeline_executesRailwayFlow", () => {
    // Arrange
    const existingUsers = new Set(["alice", "bob"]);

    const validateRequest = (req: UserRegistrationRequest): Result<UserRegistrationRequest> =>
      Guard.against
        .nullOrWhitespace(req.username, "username")
        .andThen(() =>
          Guard.against.patternMismatch(req.email, /^[^@]+@[^@]+\.[^@]+$/, "email")
        )
        .andThen(() => Guard.against.outOfRange(req.age, 18, 120, "age"))
        .transform(() => req);

    const checkUniqueness = (req: UserRegistrationRequest): Result<UserRegistrationRequest> => {
      if (existingUsers.has(req.username.toLowerCase())) {
        return fail(
          Error.fromMessages(
            "Username is already taken.",
            `Conflict: username '${req.username}' exists in repository.`,
            ErrorCategory.Conflict
          )
        );
      }
      return ok(req);
    };

    const persistUser = (req: UserRegistrationRequest): Result<RegisteredUser> =>
      ok({
        userId: 101,
        username: req.username,
        email: req.email.toLowerCase().trim(),
      });

    const registerPipeline = (req: UserRegistrationRequest): Result<RegisteredUser> =>
      validateRequest(req).andThen(checkUniqueness).andThen(persistUser);

    // Act
    const resultOk = registerPipeline({
      username: "charlie",
      email: "charlie@example.com",
      age: 25,
    });
    const resultUnderage = registerPipeline({
      username: "dave",
      email: "dave@example.com",
      age: 16,
    });
    const resultDuplicate = registerPipeline({
      username: "alice",
      email: "alice2@example.com",
      age: 30,
    });

    // Assert
    expect(resultOk.ok).toBe(true);
    if (resultOk.ok) {
      expect(resultOk.value.userId).toBe(101);
      expect(resultOk.value.email).toBe("charlie@example.com");
    }

    expect(resultUnderage.ok).toBe(false);
    expect(resultUnderage.errors[0]?.category).toBe(ErrorCategory.Validation);
    expect(resultUnderage.errors[0]?.userMessage).toContain("must be between 18 and 120");

    expect(resultDuplicate.ok).toBe(false);
    expect(resultDuplicate.errors[0]?.category).toBe(ErrorCategory.Conflict);
    expect(resultDuplicate.errors[0]?.userMessage).toBe("Username is already taken.");
  });

  it("tutorial_externalBoundary_catchesIoExceptionsViaTry", () => {
    // Arrange
    const fakeExternalPaymentGateway = (accountId: string, amountCents: number): string => {
      if (amountCents > 100000) {
        throw new globalThis.Error("Gateway TCP reset during transaction");
      }
      return `tx_ref_${accountId}_${amountCents}`;
    };

    const processPayment = (accountId: string, amountCents: number): Result<string> =>
      try_(
        () => fakeExternalPaymentGateway(accountId, amountCents),
        (exc) =>
          Error.fromMessages(
            "Payment gateway is currently unavailable. Please retry shortly.",
            `Gateway communication failure: ${String(exc)}`,
            ErrorCategory.Unavailable
          )
      );

    // Act
    const okRes = processPayment("acc_123", 5000);
    const failRes = processPayment("acc_123", 200000);

    // Assert
    expect(okRes.ok).toBe(true);
    if (okRes.ok) {
      expect(okRes.value).toBe("tx_ref_acc_123_5000");
    }

    expect(failRes.ok).toBe(false);
    expect(failRes.errors[0]?.category).toBe(ErrorCategory.Unavailable);
    expect(failRes.errors[0]?.userMessage).toContain("currently unavailable");
    expect(failRes.errors[0]?.technicalMessage).toContain("Gateway TCP reset");
  });

  it("tutorial_failFastOnProgrammerDefect_bubblesImmediately", () => {
    // Arrange
    const brokenBusinessLogic = (x: number): Result<number> => {
      const missingFn = null as unknown as (n: number) => number;
      return ok(missingFn(x));
    };

    // Act & Assert
    expect(() => ok(10).andThen(brokenBusinessLogic)).toThrow(TypeError);
  });
});
