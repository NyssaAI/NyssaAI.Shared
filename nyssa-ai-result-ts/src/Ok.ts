// Responsibility: Represent the successful arm of a railway-oriented Result discriminated union.
// Public interface:
// `-- Ok
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
//     +-- new(value)
//     +-- ok
//     +-- resolve(onSuccess, onFailure)
//     +-- resolveAsync(onSuccess, onFailure)
//     +-- throwIfError()
//     +-- transform(transformation)
//     +-- transformAsync(transformation)
//     +-- tryGetValue()
//     +-- unwrap()
//     `-- value

import type { Error } from "./Error.js";
import { ErrorCategory } from "./ErrorCategory.js";
import type { Result } from "./Result.js";

const EMPTY_ERRORS: readonly Error[] = Object.freeze([]);

/**
 * Ok holds a successfully produced value and executes pure combinators directly without
 * defensive try/catch wrapping.
 */
export class Ok<T> {
  public readonly ok: true = true;
  public readonly value: T;

  public constructor(value: T) {
    this.value = value;
    Object.freeze(this);
  }

  public get isSuccess(): true {
    return true;
  }

  public get isFailure(): false {
    return false;
  }

  public get errors(): readonly Error[] {
    return EMPTY_ERRORS;
  }

  public get error(): undefined {
    return undefined;
  }

  public get category(): ErrorCategory {
    return ErrorCategory.Unexpected;
  }

  public get message(): string {
    return "";
  }

  /** Returns the success value. */
  public throwIfError(): T {
    return this.value;
  }

  /** Returns the success value. */
  public unwrap(): T {
    return this.value;
  }

  /** Returns a tuple of [true, value]. */
  public tryGetValue(): readonly [true, T] {
    return Object.freeze([true, this.value] as const);
  }

  /**
   * Monadic bind chaining the success value into the next Result-returning operation.
   * Named andThen to prevent ECMAScript Promise/Thenable collisions and compiler Awaited unwrapping.
   * Pure combinator: does not wrap in try/catch; programmer defects bubble immediately.
   */
  public andThen<U, E = Error>(nextOperation: (value: T) => Result<U, E>): Result<U, E> {
    if (typeof nextOperation !== "function") {
      throw new TypeError("nextOperation cannot be null or undefined");
    }
    return nextOperation(this.value);
  }

  /**
   * Asynchronous monadic bind chaining the success value into an async Result-returning operation.
   * Pure combinator: does not wrap in try/catch; programmer defects bubble immediately.
   */
  public andThenAsync<U, E = Error>(
    nextOperation: (value: T) => PromiseLike<Result<U, E>> | Result<U, E>
  ): Promise<Result<U, E>> {
    return (async (): Promise<unknown> => {
      if (typeof nextOperation !== "function") {
        throw new TypeError("nextOperation cannot be null or undefined");
      }
      return await nextOperation(this.value);
    })() as Promise<Result<U, E>>;
  }

  /**
   * Functor map projecting the success value into a new Ok instance.
   * Pure combinator: does not wrap in try/catch; programmer defects bubble immediately.
   */
  public transform<U>(transformation: (value: T) => U): Ok<U> {
    if (typeof transformation !== "function") {
      throw new TypeError("transformation cannot be null or undefined");
    }
    return new Ok(transformation(this.value));
  }

  /** Alias for transform (functor map). */
  public map<U>(transformation: (value: T) => U): Ok<U> {
    return this.transform(transformation);
  }

  /**
   * Asynchronous functor map projecting the success value into a new Ok instance.
   * Pure combinator: does not wrap in try/catch; programmer defects bubble immediately.
   */
  public transformAsync<U>(
    transformation: (value: T) => PromiseLike<U> | U
  ): Promise<Ok<U>> {
    return (async (): Promise<unknown> => {
      if (typeof transformation !== "function") {
        throw new TypeError("transformation cannot be null or undefined");
      }
      return new Ok(await transformation(this.value));
    })() as Promise<Ok<U>>;
  }

  /** Alias for transformAsync (async functor map). */
  public mapAsync<U>(
    transformation: (value: T) => PromiseLike<U> | U
  ): Promise<Ok<U>> {
    return this.transformAsync(transformation);
  }

  /** Eliminates the Result by applying onSuccess to the value. */
  public resolve<R>(
    onSuccess: (value: T) => R,
    onFailure: (errors: readonly Error[]) => R
  ): R {
    if (typeof onSuccess !== "function" || typeof onFailure !== "function") {
      throw new TypeError("resolve handlers cannot be null or undefined");
    }
    return onSuccess(this.value);
  }

  /** Eliminates the Result asynchronously by applying onSuccess to the value. */
  public async resolveAsync<R>(
    onSuccess: (value: T) => PromiseLike<R> | R,
    onFailure: (errors: readonly Error[]) => PromiseLike<R> | R
  ): Promise<R> {
    if (typeof onSuccess !== "function" || typeof onFailure !== "function") {
      throw new TypeError("resolve handlers cannot be null or undefined");
    }
    return await onSuccess(this.value);
  }
}
