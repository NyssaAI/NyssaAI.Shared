// Responsibility: Construct the provider for a human-readable email representation.
// Public interface:
// `-- emailFormatterText()
import type {EmailFormatter} from './EmailFormatter.js';
/** emailFormatterText creates a stateless formatter. */
export function emailFormatterText(): EmailFormatter {
  return {format: value => `email: ${value}`};
}
