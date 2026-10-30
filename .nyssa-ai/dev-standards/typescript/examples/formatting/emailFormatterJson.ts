// Responsibility: Construct the provider for a JSON email representation.
// Public interface:
// `-- emailFormatterJson()
import type {EmailFormatter} from './EmailFormatter.js';
/** emailFormatterJson creates a stateless formatter with a single email field. */
export function emailFormatterJson(): EmailFormatter {
  return {format: value => JSON.stringify({email: value})};
}
