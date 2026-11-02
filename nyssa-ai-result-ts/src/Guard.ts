// Responsibility: Provide railway-oriented input validation preconditions returning Result outcomes.
// Public interface:
// `-- Guard
//     +-- Against
//     +-- against
//     +-- againstEmpty(value, parameterName)
//     +-- againstNull(value, parameterName)
//     +-- againstNullOrWhitespace(value, parameterName)
//     +-- againstOutOfRange(value, minVal, maxVal, parameterName)
//     +-- contains(collection, predicate, message, technicalMessage, category)
//     +-- empty(value, parameterName)
//     +-- false(conditionOrValue, predicateOrMessage, message, technicalMessage, category)
//     +-- greaterThan(value, maxVal, parameterName)
//     +-- greaterThanOrEqual(value, maxVal, parameterName)
//     +-- lengthOutOfRange(value, minLength, maxLength, parameterName)
//     +-- lessThan(value, minVal, parameterName)
//     +-- lessThanOrEqual(value, minVal, parameterName)
//     +-- none(value, parameterName)
//     +-- notContains(collection, predicate, message, technicalMessage, category)
//     +-- null(value, parameterName)
//     +-- nullOrEmpty(value, parameterName)
//     +-- nullOrWhiteSpace(value, parameterName)
//     +-- nullOrWhitespace(value, parameterName)
//     +-- outOfRange(value, minVal, maxVal, parameterName)
//     +-- patternMismatch(value, pattern, parameterName)
//     `-- true(conditionOrValue, predicateOrMessage, message, technicalMessage, category)

import { Error } from "./Error.js";
import { ErrorCategory } from "./ErrorCategory.js";
import { fail, ok, type Result } from "./Result.js";

function invalid(userMessage: string, technicalMessage: string): Error {
  return Error.fromMessages(userMessage, technicalMessage, ErrorCategory.Validation);
}

/**
 * Guard provides precondition and invariant checks returning railway-oriented Result outcomes
 * with ErrorCategory.Validation on failure.
 */
export class Guard {
  public static readonly against = Guard;
  public static readonly Against = Guard;

  /** Rejects null or undefined, returning the validated value on success. */
  public static null<T>(value: T | null | undefined, parameterName: string = "value"): Result<T, Error> {
    return value === null || value === undefined
      ? fail(invalid(`${parameterName} cannot be null`, `${parameterName} is null`))
      : ok(value);
  }

  /** Rejects null or undefined (Python parity alias for null). */
  public static none<T>(value: T | null | undefined, parameterName: string = "value"): Result<T, Error> {
    return Guard.null(value, parameterName);
  }

  /** Alias for Guard.null. */
  public static againstNull<T>(value: T | null | undefined, parameterName: string = "value"): Result<T, Error> {
    return Guard.null(value, parameterName);
  }

  /** Rejects null, undefined, or an empty string or array. */
  public static nullOrEmpty<T extends string | readonly unknown[]>(
    value: T | null | undefined,
    parameterName: string = "value"
  ): Result<T, Error> {
    if (value === null || value === undefined) {
      return fail(invalid(`${parameterName} cannot be empty`, `${parameterName} is null`));
    }
    return value.length === 0
      ? fail(invalid(`${parameterName} cannot be empty`, `${parameterName} is empty`))
      : ok(value);
  }

  /** Alias for Guard.nullOrEmpty. */
  public static empty<T extends string | readonly unknown[]>(
    value: T | null | undefined,
    parameterName: string = "value"
  ): Result<T, Error> {
    return Guard.nullOrEmpty(value, parameterName);
  }

  /** Alias for Guard.nullOrEmpty. */
  public static againstEmpty<T extends string | readonly unknown[]>(
    value: T | null | undefined,
    parameterName: string = "value"
  ): Result<T, Error> {
    return Guard.nullOrEmpty(value, parameterName);
  }

  /** Rejects null, undefined, empty, or whitespace-only string. */
  public static nullOrWhitespace(value: string | null | undefined, parameterName: string = "value"): Result<string, Error> {
    if (value === null || value === undefined) {
      return fail(invalid(`${parameterName} cannot be empty`, `${parameterName} is null`));
    }
    return value.trim().length === 0
      ? fail(invalid(`${parameterName} cannot be empty`, `${parameterName} is whitespace`))
      : ok(value);
  }

  /** Alias for Guard.nullOrWhitespace (C# casing parity). */
  public static nullOrWhiteSpace(value: string | null | undefined, parameterName: string = "value"): Result<string, Error> {
    return Guard.nullOrWhitespace(value, parameterName);
  }

  /** Alias for Guard.nullOrWhitespace. */
  public static againstNullOrWhitespace(value: string | null | undefined, parameterName: string = "value"): Result<string, Error> {
    return Guard.nullOrWhitespace(value, parameterName);
  }

  /** Validates that value falls within inclusive range [minVal, maxVal]. */
  public static outOfRange<T extends number | bigint | string | Date>(
    value: T | null | undefined,
    minVal: T,
    maxVal: T,
    parameterName: string = "value"
  ): Result<T, Error> {
    if (value === null || value === undefined) {
      return fail(invalid(`${parameterName} cannot be null`, `${parameterName} is null`));
    }
    return value < minVal || value > maxVal
      ? fail(
          invalid(
            `${parameterName} must be between ${String(minVal)} and ${String(maxVal)}`,
            `${parameterName} value ${String(value)} is outside range [${String(minVal)}, ${String(maxVal)}]`
          )
        )
      : ok(value);
  }

  /** Alias for Guard.outOfRange. */
  public static againstOutOfRange<T extends number | bigint | string | Date>(
    value: T | null | undefined,
    minVal: T,
    maxVal: T,
    parameterName: string = "value"
  ): Result<T, Error> {
    return Guard.outOfRange(value, minVal, maxVal, parameterName);
  }

  /** Rejects value strictly less than minVal (requires value >= minVal). */
  public static lessThan<T extends number | bigint | string | Date>(
    value: T | null | undefined,
    minVal: T,
    parameterName: string = "value"
  ): Result<T, Error> {
    if (value === null || value === undefined) {
      return fail(invalid(`${parameterName} cannot be null`, `${parameterName} is null`));
    }
    return value < minVal
      ? fail(
          invalid(
            `${parameterName} must be greater than or equal to ${String(minVal)}`,
            `${parameterName} value ${String(value)} is less than ${String(minVal)}`
          )
        )
      : ok(value);
  }

  /** Rejects value less than or equal to minVal (requires value > minVal). */
  public static lessThanOrEqual<T extends number | bigint | string | Date>(
    value: T | null | undefined,
    minVal: T,
    parameterName: string = "value"
  ): Result<T, Error> {
    if (value === null || value === undefined) {
      return fail(invalid(`${parameterName} cannot be null`, `${parameterName} is null`));
    }
    return value <= minVal
      ? fail(
          invalid(
            `${parameterName} must be greater than ${String(minVal)}`,
            `${parameterName} value ${String(value)} is not greater than ${String(minVal)}`
          )
        )
      : ok(value);
  }

  /** Rejects value strictly greater than maxVal (requires value <= maxVal). */
  public static greaterThan<T extends number | bigint | string | Date>(
    value: T | null | undefined,
    maxVal: T,
    parameterName: string = "value"
  ): Result<T, Error> {
    if (value === null || value === undefined) {
      return fail(invalid(`${parameterName} cannot be null`, `${parameterName} is null`));
    }
    return value > maxVal
      ? fail(
          invalid(
            `${parameterName} must be less than or equal to ${String(maxVal)}`,
            `${parameterName} value ${String(value)} exceeds ${String(maxVal)}`
          )
        )
      : ok(value);
  }

  /** Rejects value greater than or equal to maxVal (requires value < maxVal). */
  public static greaterThanOrEqual<T extends number | bigint | string | Date>(
    value: T | null | undefined,
    maxVal: T,
    parameterName: string = "value"
  ): Result<T, Error> {
    if (value === null || value === undefined) {
      return fail(invalid(`${parameterName} cannot be null`, `${parameterName} is null`));
    }
    return value >= maxVal
      ? fail(
          invalid(
            `${parameterName} must be less than ${String(maxVal)}`,
            `${parameterName} value ${String(value)} is not less than ${String(maxVal)}`
          )
        )
      : ok(value);
  }

  /** Rejects null/undefined, invalid bounds, or string length outside [minLength, maxLength]. */
  public static lengthOutOfRange(
    value: string | null | undefined,
    minLength: number,
    maxLength: number,
    parameterName: string = "value"
  ): Result<string, Error> {
    if (value === null || value === undefined) {
      return fail(invalid(`${parameterName} cannot be null`, `${parameterName} is null`));
    }
    if (minLength < 0 || maxLength < minLength) {
      return fail(invalid(`${parameterName} length bounds are invalid`, "Length bounds must be nonnegative and ordered"));
    }
    return value.length < minLength || value.length > maxLength
      ? fail(
          invalid(
            `${parameterName} must be between ${minLength} and ${maxLength} characters`,
            `${parameterName} length ${value.length} is outside range [${minLength}, ${maxLength}]`
          )
        )
      : ok(value);
  }

  /** Rejects null/undefined or string not matching pattern. */
  public static patternMismatch(
    value: string | null | undefined,
    pattern: string | RegExp,
    parameterName: string = "value"
  ): Result<string, Error> {
    if (value === null || value === undefined) {
      return fail(invalid(`${parameterName} format is invalid`, `${parameterName} is null`));
    }
    try {
      const regex = typeof pattern === "string" ? new RegExp(pattern) : pattern;
      return regex.test(value)
        ? ok(value)
        : fail(invalid(`${parameterName} format is invalid`, `${parameterName} value '${value}' does not match pattern '${String(pattern)}'`));
    } catch (exc) {
      return fail(invalid(`${parameterName} could not be checked.`, `${parameterName} pattern '${String(pattern)}' is not valid: ${String(exc)}`));
    }
  }

  /** Rejects a true condition or a value whose predicate evaluates to true. */
  public static true<T = undefined>(
    conditionOrValue: T,
    predicateOrMessage: ((value: T) => boolean) | string,
    message?: string | null,
    technicalMessage?: string | null,
    category: ErrorCategory | string = ErrorCategory.Validation
  ): Result<T, Error> {
    if (typeof predicateOrMessage === "function") {
      const msg = message ?? "Condition is invalid";
      const tech = technicalMessage ?? msg;
      try {
        return predicateOrMessage(conditionOrValue) ? fail(Error.fromMessages(msg, tech, category)) : ok(conditionOrValue);
      } catch (exc) {
        return fail(Error.fromCaught(exc)[0]!);
      }
    }
    const msgStr = String(predicateOrMessage);
    return Boolean(conditionOrValue) ? fail(Error.fromMessages(msgStr, technicalMessage ?? msgStr, category)) : ok(undefined as T);
  }

  /** Rejects a false condition or a value whose predicate evaluates to false. */
  public static false<T = undefined>(
    conditionOrValue: T,
    predicateOrMessage: ((value: T) => boolean) | string,
    message?: string | null,
    technicalMessage?: string | null,
    category: ErrorCategory | string = ErrorCategory.Validation
  ): Result<T, Error> {
    if (typeof predicateOrMessage === "function") {
      const msg = message ?? "Condition is invalid";
      const tech = technicalMessage ?? msg;
      try {
        return !predicateOrMessage(conditionOrValue) ? fail(Error.fromMessages(msg, tech, category)) : ok(conditionOrValue);
      } catch (exc) {
        return fail(Error.fromCaught(exc)[0]!);
      }
    }
    const msgStr = String(predicateOrMessage);
    return !Boolean(conditionOrValue) ? fail(Error.fromMessages(msgStr, technicalMessage ?? msgStr, category)) : ok(undefined as T);
  }

  /** Rejects a collection containing any item matching predicate. */
  public static contains<C extends Iterable<unknown>>(
    collection: C | null | undefined,
    predicate: ((item: C extends Iterable<infer T> ? T : never) => boolean) | null | undefined,
    message: string,
    technicalMessage?: string | null,
    category: ErrorCategory | string = ErrorCategory.Validation
  ): Result<C, Error> {
    if (collection === null || collection === undefined) return fail(invalid(message, "The collection cannot be null."));
    if (typeof predicate !== "function") return fail(invalid(message, "The predicate cannot be null."));
    try {
      for (const item of collection) {
        if ((predicate as (item: unknown) => boolean)(item)) return fail(Error.fromMessages(message, technicalMessage ?? message, category));
      }
      return ok(collection);
    } catch (exc) {
      return fail(Error.fromCaught(exc)[0]!);
    }
  }

  /** Rejects a collection with no item matching predicate. */
  public static notContains<C extends Iterable<unknown>>(
    collection: C | null | undefined,
    predicate: ((item: C extends Iterable<infer T> ? T : never) => boolean) | null | undefined,
    message: string,
    technicalMessage?: string | null,
    category: ErrorCategory | string = ErrorCategory.Validation
  ): Result<C, Error> {
    if (collection === null || collection === undefined) return fail(invalid(message, "The collection cannot be null."));
    if (typeof predicate !== "function") return fail(invalid(message, "The predicate cannot be null."));
    try {
      for (const item of collection) {
        if ((predicate as (item: unknown) => boolean)(item)) return ok(collection);
      }
      return fail(Error.fromMessages(message, technicalMessage ?? message, category));
    } catch (exc) {
      return fail(Error.fromCaught(exc)[0]!);
    }
  }
}
