// Responsibility: Represent the failed arm of a railway-oriented Result discriminated union.
// Public interface:
// `-- Err
//     +-- andThen(nextOperation)
//     +-- andThenAsync(nextOperation)
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
//     +-- throwIfError()
//     +-- transform(transformation)
//     +-- transformAsync(transformation)
//     +-- tryGetValue()
//     +-- unwrap()
//     `-- value

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
    } else if (typeof error === "string") {
      this._errors = Object.freeze([Error.fromMessage(error)]);
    } else if (
      typeof error === "object" &&
      error !== null &&
      "userMessage" in error &&
      typeof (error as { readonly userMessage?: unknown }).userMessage === "string"
    ) {
      const candidate = error as {
        readonly userMessage: string;
        readonly technicalMessage?: string | null;
        readonly category?: ErrorCategory | string;
        readonly causeType?: string | null;
      };
      this._errors = Object.freeze([
        Error.fromMessages(
          candidate.userMessage,
          candidate.technicalMessage ?? candidate.userMessage,
          candidate.category ?? ErrorCategory.Unexpected,
          candidate.causeType ?? null
        ),
      ]);
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

  /**
   * Safe undefined accessor for uniform property access across union arms.
   */
  public get value(): undefined {
    return undefined;
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
  public andThen<U, F = E>(nextOperation: (value: never) => Result<U, F>): Err<E> {
    if (typeof nextOperation !== "function") {
      throw new TypeError("nextOperation cannot be null or undefined");
    }
    return this;
  }

  /** Short-circuits and propagates this Err asynchronously without executing nextOperation. */
  public andThenAsync<U, F = E>(
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
