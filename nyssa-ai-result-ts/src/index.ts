// Responsibility: Export the public surface of nyssa-ai-result.
// Public interface:
// +-- CANCELED_USER_MESSAGE
// +-- DEFAULT_USER_MESSAGE
// +-- Err
// +-- Error
// +-- ErrorCategory
// +-- Guard
// +-- Ok
// +-- Result
// +-- ResultException
// +-- TIMED_OUT_USER_MESSAGE
// +-- fail
// +-- failError
// +-- index
// +-- ok
// +-- tryAsync
// `-- try_

import { Err as ErrClass } from "./Err.js";
import {
  CANCELED_USER_MESSAGE as CanceledUserMsg,
  DEFAULT_USER_MESSAGE as DefaultUserMsg,
  Error as ErrorClass,
  TIMED_OUT_USER_MESSAGE as TimedOutUserMsg,
} from "./Error.js";
import { ErrorCategory as ErrorCategoryEnum } from "./ErrorCategory.js";
import { Guard as GuardClass } from "./Guard.js";
import { Ok as OkClass } from "./Ok.js";
import {
  fail as failFn,
  failError as failErrorFn,
  ok as okFn,
  Result as ResultCompanion,
  try_ as tryFn,
  tryAsync as tryAsyncFn,
} from "./Result.js";
import { ResultException as ResultExceptionClass } from "./ResultException.js";

/** Default user message for unexpected errors. */
export const DEFAULT_USER_MESSAGE = DefaultUserMsg;

/** Default user message for cancelled operations. */
export const CANCELED_USER_MESSAGE = CanceledUserMsg;

/** Default user message for timed-out operations. */
export const TIMED_OUT_USER_MESSAGE = TimedOutUserMsg;

/** Failure category constants. */
export const ErrorCategory = ErrorCategoryEnum;

/** Immutable failure value object class. */
export const Error = ErrorClass;

/** Exception thrown when unwrapping a failed Result. */
export const ResultException = ResultExceptionClass;

/** Successful Result arm class. */
export const Ok = OkClass;

/** Failed Result arm class. */
export const Err = ErrClass;

/** Result companion namespace. */
export const Result = ResultCompanion;

/** Railway-oriented precondition guards. */
export const Guard = GuardClass;

/** Factory wrapping a value in an Ok arm. */
export const ok = okFn;

/** Factory wrapping an error or message in an Err arm. */
export const fail = failFn;

/** Factory wrapping a custom domain error in an Err arm. */
export const failError = failErrorFn;

/** Synchronous explicit exception boundary wrapper. */
export const try_ = tryFn;

/** Asynchronous explicit exception boundary wrapper. */
export const tryAsync = tryAsyncFn;

/** Package barrel index reference. */
export const index = ResultCompanion;
