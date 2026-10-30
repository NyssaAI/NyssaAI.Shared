// Responsibility: Validate and normalize an address into a branded immutable value.
// Public interface:
// +-- EmailAddress
// |   `-- __brand
// +-- MAX_EMAIL_LENGTH
// `-- toEmailAddress(value)
import {fail, ok, type Result} from '../results/Result.js';
/** EmailAddress is validated under this sample's bounded ASCII policy, not full RFC validation. */
export type EmailAddress = string & {
  /** __brand distinguishes parsed addresses at compile time; it is not a runtime property. */
  readonly __brand: 'EmailAddress';
};
/** MAX_EMAIL_LENGTH bounds normalized input length. */
export const MAX_EMAIL_LENGTH = 254;
/** toEmailAddress rejects malformed addresses and normalizes domain casing. */
export function toEmailAddress(value: string): Result<EmailAddress> {
  const candidate = value.trim();
  if (candidate.length > MAX_EMAIL_LENGTH || !/^[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+$/.test(candidate)) return fail('email.invalid');
  const at = candidate.indexOf('@');
  return ok(`${candidate.slice(0, at)}@${candidate.slice(at + 1).toLowerCase()}` as EmailAddress);
}
