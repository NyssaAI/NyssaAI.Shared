// Responsibility: Represent the failed arm of a railway-oriented Result discriminated union.
// Public interface:
// `-- Err
//     +-- category
//     +-- error
//     +-- errors
//     +-- isFailure
//     +-- isSuccess
//     +-- map(transformation)
//     +-- mapAsync(transformation)
//     +-- message
//     +-- new(error, allErrors)
//     +-- ok
//     +-- resolve(onSuccess, onFailure)
//     +-- resolveAsync(onSuccess, onFailure)
//     +-- then(nextOperation)
//     +-- thenAsync(nextOperation)
//     +-- throwIfError()
//     +-- transform(transformation)
//     +-- transformAsync(transformation)
//     +-- tryGetValue()
//     `-- unwrap()

import { Error } from "./Error.js";
import { ErrorCategory } from "./ErrorCategory.js";
import type { Result } from "./Result.js";
import { ResultException } from "./ResultException.js";

/**
 * Err holds a caller-actionable failure or structured Error and short-circuits subsequent
 * railway combinators without invoking their callbacks.
 */
export class Err<E = Error> {
  public readonly ok: false = false;
  public readonly error: E;
  private readonly _errors: readonly Error[];

  public constructor(error: E, allErrors?: readonly Error[]) {
    this.error = error;
    if (allErrors !== undefined && allErrors.length > 0) {
      this._errors = Object.freeze([...allErrors]);
    } else if (error instanceof Error) {
      this._errors = Object.freeze([error]);
    } else if (Array.isArray(error)) {
      this._errors = Error.atLeastOne(error as readonly Error[]);
    } else if (typeof error === "object" && error !== null && "message" in error) {
      const candidate = error as {
        readonly message?: unknown;
        readonly category?: unknown;
        readonly causeType?: unknown;
      };
      const msg = String(candidate.message ?? "");
      const cat =
        typeof candidate.category === "string"
          ? candidate.category
          : ErrorCategory.Unexpected;
      const causeType =
        typeof candidate.causeType === "string" ? candidate.causeType : null;
      this._errors = Object.freeze([Error.fromMessages(msg, msg, cat, causeType)]);
    } else {
      this._errors = Object.freeze([Error.fromMessage(String(error))]);
    }
    Object.freeze(this);
  }

  public get isSuccess(): false {
    return false;
  }

  public get isFailure(): true {
    return true;
  }

  public get errors(): readonly Error[] {
    return this._errors;
  }

  public get category(): ErrorCategory | string {
    if (this.error instanceof Error) {
      return this.error.category;
    }
    if (typeof this.error === "object" && this.error !== null && "category" in this.error) {
      const cat = (this.error as { readonly category?: unknown }).category;
      if (typeof cat === "string") {
        return cat;
      }
    }
    return ErrorCategory.Unexpected;
  }

  public get message(): string {
    if (this.error instanceof Error) {
      return this.error.message;
    }
    if (typeof this.error === "object" && this.error !== null && "message" in this.error) {
      return String((this.error as { readonly message?: unknown }).message ?? "");
    }
    return String(this.error);
  }

  /** Explicitly throws a ResultException carrying all structured errors. */
  public throwIfError(): never {
    throw new ResultException(this._errors);
  }

  /** Explicitly throws a ResultException carrying all structured errors. */
  public unwrap(): never {
    throw new ResultException(this._errors);
  }

  /** Returns a tuple of [false, undefined]. */
  public tryGetValue(): readonly [false, undefined] {
    return Object.freeze([false, undefined] as const);
  }

  /** Short-circuits and propagates this Err without executing nextOperation. */
  public then<U, F = E>(nextOperation: (value: never) => Result<U, F>): Err<E>;
  public then(
    nextOperation: (value: {
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
    }) => unknown
  ): unknown;
  public then<U, F = E>(nextOperation: (value: never) => Result<U, F>): unknown {
    if (typeof nextOperation !== "function") {
      throw new TypeError("nextOperation cannot be null or undefined");
    }
    return this;
  }

  /** Short-circuits and propagates this Err asynchronously without executing nextOperation. */
  public thenAsync<U, F = E>(
    nextOperation: (value: never) => PromiseLike<Result<U, F>> | Result<U, F>
  ): Promise<Err<E>> {
    if (typeof nextOperation !== "function") {
      return Promise.reject(new TypeError("nextOperation cannot be null or undefined"));
    }
    return Promise.resolve(this);
  }

  /** Short-circuits and propagates this Err without executing transformation. */
  public transform<U>(transformation: (value: never) => U): Err<E> {
    if (typeof transformation !== "function") {
      throw new TypeError("transformation cannot be null or undefined");
    }
    return this;
  }

  /** Alias for transform. */
  public map<U>(transformation: (value: never) => U): Err<E> {
    return this.transform(transformation);
  }

  /** Short-circuits and propagates this Err asynchronously without executing transformation. */
  public transformAsync<U>(
    transformation: (value: never) => PromiseLike<U> | U
  ): Promise<Err<E>> {
    if (typeof transformation !== "function") {
      return Promise.reject(new TypeError("transformation cannot be null or undefined"));
    }
    return Promise.resolve(this);
  }

  /** Alias for transformAsync. */
  public mapAsync<U>(
    transformation: (value: never) => PromiseLike<U> | U
  ): Promise<Err<E>> {
    return this.transformAsync(transformation);
  }

  /** Eliminates the Result by applying onFailure to the accumulated errors. */
  public resolve<R>(
    onSuccess: (value: never) => R,
    onFailure: (errors: readonly Error[]) => R
  ): R {
    if (typeof onSuccess !== "function" || typeof onFailure !== "function") {
      throw new TypeError("resolve handlers cannot be null or undefined");
    }
    return onFailure(this._errors);
  }

  /** Eliminates the Result asynchronously by applying onFailure to the accumulated errors. */
  public async resolveAsync<R>(
    onSuccess: (value: never) => PromiseLike<R> | R,
    onFailure: (errors: readonly Error[]) => PromiseLike<R> | R
  ): Promise<R> {
    if (typeof onSuccess !== "function" || typeof onFailure !== "function") {
      throw new TypeError("resolve handlers cannot be null or undefined");
    }
    return await onFailure(this._errors);
  }
}

const resolvingErr = new WeakSet<object>();

Object.defineProperty(Err.prototype, "then", {
  configurable: true,
  enumerable: false,
  get(this: Err<unknown>) {
    if (resolvingErr.has(this)) {
      resolvingErr.delete(this);
      return undefined;
    }
    return <U, F = unknown>(
      nextOperation: (value: never) => Result<U, F>,
      onRejected?: unknown
    ): unknown => {
      if (typeof onRejected === "function") {
        resolvingErr.add(this);
        return (nextOperation as (v: unknown) => unknown)(this);
      }
      if (typeof nextOperation !== "function") {
        throw new TypeError("nextOperation cannot be null or undefined");
      }
      return this;
    };
  },
});
