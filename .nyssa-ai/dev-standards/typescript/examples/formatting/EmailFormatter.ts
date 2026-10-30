// Responsibility: Define the format-provider contract consumed by the example workflow.
// Public interface:
// `-- EmailFormatter
//     `-- format(value)
import type {EmailAddress} from '../email/EmailAddress.js';
/** EmailFormatter renders an address without mutation or I/O; output preserves its exact normalized text. */
export interface EmailFormatter {
  /** format returns the provider-specific representation. */
  format(value: EmailAddress): string;
}
