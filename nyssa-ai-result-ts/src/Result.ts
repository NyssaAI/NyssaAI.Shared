// Responsibility: Provide railway-oriented discriminated-union Result with pure combinators and explicit boundaries.
// Public interface:
// +-- Err
// +-- Ok
// +-- Result
// |   +-- andThen(nextOperation)
// |   +-- andThenAsync(nextOperation)
// |   +-- category
// |   +-- error
// |   +-- errors
// |   +-- isFailure
// |   +-- isSuccess
// |   +-- map(transformation)
// |   +-- mapAsync(transformation)
// |   +-- message
// |   +-- ok
// |   +-- resolve(onSuccess, onFailure)
// |   +-- resolveAsync(onSuccess, onFailure)
// |   +-- throwIfError()
// |   +-- transform(transformation)
// |   +-- transformAsync(transformation)
// |   +-- tryGetValue()
// |   +-- unwrap()
// |   `-- value
// +-- andThen(result, nextOperation)
// +-- fail(errorOrErrors, category, cause)
// +-- failError(error)
// +-- ok(value)
// +-- tryAsync(action, errorHandler, cancellationToken)
// `-- try_(action, errorHandler, cancellationToken)

import { Err as ErrClass } from "./Err.js";
import { Error } from "./Error.js";
import { ErrorCategory } from "./ErrorCategory.js";
import { Ok as OkClass } from "./Ok.js";

function extractCauseType(cause: unknown): string | null {
  if (cause === null || cause === undefined) {
    return null;
  }
  if (typeof cause === "object") {
    const obj = cause as { readonly name?: unknown; readonly constructor?: { readonly name?: string } };
    if (typeof obj.name === "string" && obj.name.length > 0 && obj.name !== "Error" && obj.name !== "DOMException") {
      return obj.name;
    }
    if (
      obj.constructor &&
      typeof obj.constructor.name === "string" &&
      obj.constructor.name.length > 0 &&
      obj.constructor.name !== "Object" &&
      obj.constructor.name !== "Error"
    ) {
      return obj.constructor.name;
    }
    if (typeof obj.name === "string" && obj.name.length > 0) {
      return obj.name;
    }
    if (obj.constructor && typeof obj.constructor.name === "string" && obj.constructor.name.length > 0) {
      return obj.constructor.name;
    }
  }
  return typeof cause;
}

function isAbortOrTimeout(exc: unknown, cancellationToken: unknown): boolean {
  if (cancellationToken === true) {
    return true;
  }
  if (typeof cancellationToken === "object" && cancellationToken !== null) {
    const tok = cancellationToken as {
      readonly aborted?: unknown;
      readonly isCancellationRequested?: unknown;
      readonly is_cancellation_requested?: unknown;
      readonly isSet?: unknown;
      readonly is_set?: unknown;
    };
    if (
      tok.aborted === true ||
      tok.isCancellationRequested === true ||
      tok.is_cancellation_requested === true ||
      (typeof tok.isSet === "function" && Boolean((tok.isSet as () => unknown)())) ||
      (typeof tok.is_set === "function" && Boolean((tok.is_set as () => unknown)()))
    ) {
      return true;
    }
  }
  if (exc !== null && typeof exc === "object") {
    const typeName = (extractCauseType(exc) ?? "").toLowerCase();
    if (
      typeName === "aborterror" ||
      typeName === "cancelederror" ||
      typeName === "cancellederror" ||
      typeName === "operationcanceledexception" ||
      typeName === "taskcanceledexception" ||
      typeName === "timeouterror" ||
      typeName === "timeoutexception"
    ) {
      return true;
    }
    const code = (exc as { readonly code?: unknown }).code;
    if (code === "ETIMEDOUT" || code === "UND_ERR_CONNECT_TIMEOUT") {
      return true;
    }
  }
  return false;
}

/** Re-export Ok class constructor for callers importing from Result. */
export const Ok = OkClass;

/** Re-export Err class constructor for callers importing from Result. */
export const Err = ErrClass;

/**
 * Wraps a successful value in an immutable Ok arm.
 */
export function ok<T = undefined>(value?: T): OkClass<T> {
  return new OkClass(value as T);
}

/**
 * Wraps a message, category, Error, or sequence of Errors in an immutable Err arm.
 */
export function fail(
  errorOrErrors: string | Error | Iterable<Error | null | undefined> | null | undefined = undefined,
  category: ErrorCategory | string = ErrorCategory.Validation,
  cause: unknown = null
): ErrClass<Error> {
  if (errorOrErrors instanceof Error) {
    return new ErrClass(errorOrErrors);
  }
  if (typeof errorOrErrors === "string") {
    const err = Error.fromMessages(
      errorOrErrors,
      errorOrErrors,
      category,
      extractCauseType(cause)
    );
    return new ErrClass(err);
  }
  if (errorOrErrors !== null && errorOrErrors !== undefined) {
    const allErrors = Error.atLeastOne(errorOrErrors);
    return new ErrClass(allErrors[0]!, allErrors);
  }
  return new ErrClass(Error.fromMessage(null, category));
}

/**
 * Wraps an arbitrary domain error value in an immutable Err arm.
 */
export function failError<const E>(error: E): ErrClass<E> {
  return new ErrClass(error);
}

/**
 * Explicit boundary executing a synchronous action and translating thrown exceptions into an Err.
 */
export function try_<T>(
  action: () => T,
  errorHandler: ((error: unknown) => Error) | null | undefined = undefined,
  cancellationToken: unknown = null
): Result<T, Error> {
  if (typeof action !== "function") {
    throw new TypeError("action cannot be null or undefined");
  }
  try {
    return new OkClass(action());
  } catch (exc) {
    if (isAbortOrTimeout(exc, cancellationToken)) {
      return new ErrClass(Error.fromCancellation(exc, cancellationToken));
    }
    if (errorHandler !== null && errorHandler !== undefined) {
      try {
        return new ErrClass(errorHandler(exc));
      } catch (handlerExc) {
        const caught = Error.fromCaught(handlerExc, cancellationToken);
        return new ErrClass(caught[0]!, caught);
      }
    }
    const caught = Error.fromCaught(exc, cancellationToken);
    return new ErrClass(caught[0]!, caught);
  }
}

/**
 * Explicit boundary executing an asynchronous action and translating thrown exceptions into an Err.
 */
export function tryAsync<T>(
  action: () => T | PromiseLike<T>,
  errorHandler: ((error: unknown) => Error) | null | undefined = undefined,
  cancellationToken: unknown = null
): Promise<Result<T, Error>> {
  return (async (): Promise<unknown> => {
    if (typeof action !== "function") {
      throw new TypeError("action cannot be null or undefined");
    }
    try {
      const value = await action();
      return new OkClass(value);
    } catch (exc) {
      if (isAbortOrTimeout(exc, cancellationToken)) {
        return new ErrClass(Error.fromCancellation(exc, cancellationToken));
      }
      if (errorHandler !== null && errorHandler !== undefined) {
        try {
          return new ErrClass(errorHandler(exc));
        } catch (handlerExc) {
          const caught = Error.fromCaught(handlerExc, cancellationToken);
          return new ErrClass(caught[0]!, caught);
        }
      }
      const caught = Error.fromCaught(exc, cancellationToken);
      return new ErrClass(caught[0]!, caught);
    }
  })() as Promise<Result<T, Error>>;
}

/**
 * Chains a fallible operation on a Result instance.
 */
export function andThen<T, U, E = Error, F = E>(
  result: Result<T, E>,
  nextOperation: (value: T) => Result<U, F>
): Result<U, E | F> {
  if (result.ok) {
    return nextOperation(result.value);
  }
  return result as unknown as Result<U, E | F>;
}

const resultCompanion = Object.freeze({
  ok,
  fail,
  failError,
  success: ok,
  failure: fail,
  try: try_,
  try_,
  tryAsync,
  andThen,
});

/**
 * Result companion namespace providing static factories and explicit exception boundary wrappers.
 */
export const Result = resultCompanion;

/**
 * Discriminated union representing either a successful Ok<T> or a failed Err<E>.
 */
export type Result<T, E = Error> =
  | {
      readonly ok: true;
      readonly value: T;
      readonly isSuccess: true;
      readonly isFailure: false;
      readonly errors: readonly Error[];
      readonly error: undefined;
      readonly category: ErrorCategory | string;
      readonly message: string;
      throwIfError(): T;
      unwrap(): T;
      tryGetValue(): readonly [true, T];
      andThen<U, F = E>(nextOperation: (value: T) => Result<U, F>): Result<U, E | F>;
      andThenAsync<U, F = E>(
        nextOperation: (value: T) => PromiseLike<Result<U, F>> | Result<U, F>
      ): Promise<Result<U, E | F>>;
      transform<U>(transformation: (value: T) => U): Result<U, E>;
      map<U>(transformation: (value: T) => U): Result<U, E>;
      transformAsync<U>(
        transformation: (value: T) => PromiseLike<U> | U
      ): Promise<Result<U, E>>;
      mapAsync<U>(
        transformation: (value: T) => PromiseLike<U> | U
      ): Promise<Result<U, E>>;
      resolve<R>(
        onSuccess: (value: T) => R,
        onFailure: (errors: readonly Error[]) => R
      ): R;
      resolveAsync<R>(
        onSuccess: (value: T) => PromiseLike<R> | R,
        onFailure: (errors: readonly Error[]) => PromiseLike<R> | R
      ): Promise<R>;
    }
  | {
      readonly ok: false;
      readonly error: E;
      readonly isSuccess: false;
      readonly isFailure: true;
      readonly errors: readonly Error[];
      readonly category: ErrorCategory | string;
      readonly message: string;
      throwIfError(): never;
      unwrap(): never;
      tryGetValue(): readonly [false, undefined];
      andThen<U, F = E>(nextOperation: (value: never) => Result<U, F>): Result<U, E | F>;
      andThenAsync<U, F = E>(
        nextOperation: (value: never) => PromiseLike<Result<U, F>> | Result<U, F>
      ): Promise<Result<U, E | F>>;
      transform<U>(transformation: (value: never) => U): Result<U, E>;
      map<U>(transformation: (value: never) => U): Result<U, E>;
      transformAsync<U>(
        transformation: (value: never) => PromiseLike<U> | U
      ): Promise<Result<U, E>>;
      mapAsync<U>(
        transformation: (value: never) => PromiseLike<U> | U
      ): Promise<Result<U, E>>;
      resolve<R>(
        onSuccess: (value: never) => R,
        onFailure: (errors: readonly Error[]) => R
      ): R;
      resolveAsync<R>(
        onSuccess: (value: never) => PromiseLike<R> | R,
        onFailure: (errors: readonly Error[]) => PromiseLike<R> | R
      ): Promise<R>;
    };
