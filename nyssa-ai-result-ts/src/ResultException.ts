// Responsibility: Carry structured errors from an explicitly thrown or unwrapped failed result.
// Public interface:
// `-- ResultException
//     +-- errors
//     +-- message
//     +-- name
//     +-- new(errors)
//     `-- stack

import { Error } from "./Error.js";

/**
 * ResultException carries an immutable snapshot of structured errors when a failed Result is
 * explicitly unwrapped or thrown.
 */
export class ResultException {
  public readonly name: string = "ResultException";
  public readonly message: string;
  public readonly stack: string | undefined;
  public readonly errors: readonly Error[];

  public constructor(errors?: Iterable<Error | null | undefined> | Error | null) {
    const normalized =
      errors instanceof Error ? Object.freeze([errors]) : Error.atLeastOne(errors);
    this.errors = normalized;
    const messages = normalized.map((e) => e.userMessage);
    this.message = messages.length > 0 ? messages.join("\n") : "Result failure";
    const captured = new globalThis.Error(this.message);
    this.stack = captured.stack;
    Object.setPrototypeOf(this, ResultException.prototype);
  }
}

Object.setPrototypeOf(ResultException.prototype, globalThis.Error.prototype);
