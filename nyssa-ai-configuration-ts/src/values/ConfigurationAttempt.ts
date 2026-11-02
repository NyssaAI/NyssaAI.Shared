// Responsibility: Keep a Result and its partial operation progress or recovery handle together.
// Public interface:
// `-- ConfigurationAttempt
//     +-- category
//     +-- error
//     +-- errors
//     +-- isFailure
//     +-- isSuccess
//     +-- message
//     +-- new(result, progress, recoveryHandle)
//     +-- ok
//     +-- progress
//     +-- recoveryHandle
//     +-- result
//     +-- throwIfError()
//     `-- value

import type { Error, Result } from "@nyssa-ai/result";
import type { ConfigurationOperationProgress } from "../recovery/Types.js";
import type { ConfigurationRecoveryHandle } from "../recovery/ConfigurationRecoveryHandle.js";

/**
 * ConfigurationAttempt pairs an operation Result with optional progress receipts and recovery handles.
 */
export class ConfigurationAttempt<T> {
  public readonly result: Result<T>;
  public readonly progress?: ConfigurationOperationProgress | undefined;
  public readonly recoveryHandle?: ConfigurationRecoveryHandle | undefined;

  public constructor(
    result: Result<T>,
    progress?: ConfigurationOperationProgress | undefined,
    recoveryHandle?: ConfigurationRecoveryHandle | undefined
  ) {
    this.result = result;
    this.progress = progress;
    this.recoveryHandle = recoveryHandle;
  }

  public get ok(): boolean {
    return this.result.ok;
  }

  public get isSuccess(): boolean {
    return this.result.isSuccess;
  }

  public get isFailure(): boolean {
    return this.result.isFailure;
  }

  public get value(): T {
    return (this.result as { readonly value: T }).value;
  }

  public get error(): string {
    const err = (this.result as { readonly error?: unknown }).error;
    if (typeof err === "string") {
      return err;
    }
    if (err && typeof err === "object" && "userMessage" in err) {
      return String((err as { readonly userMessage: string }).userMessage);
    }
    return String(err ?? "");
  }

  public get category(): string | undefined {
    return this.result.category;
  }

  public get message(): string {
    return this.result.message;
  }

  public get errors(): readonly Error[] {
    return this.result.errors;
  }

  public throwIfError(): T {
    return this.result.throwIfError();
  }
}
