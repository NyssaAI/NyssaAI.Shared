// Responsibility: Data contract for one stored configuration value matching .NET ConfigurationValue.
// Public interface:
// `-- ConfigurationValue
//     +-- encrypted
//     +-- json
//     +-- provider
//     +-- reauth_required
//     `-- value

/**
 * ConfigurationValue models a single setting entry in a namespace document.
 * Unencrypted literals hold their raw JSON data; encrypted or provider-backed entries hold strings.
 */
export interface ConfigurationValue {
  /** The value: raw data for literals, string for encrypted or provider pointers. */
  readonly value: unknown;
  /** Whether the value is sealed by a cipher. */
  readonly encrypted?: boolean;
  /** The provider name (e.g. 'aesLocal'), omitted for literal values. */
  readonly provider?: string;
  /** Whether the string was originally a JSON object/array. */
  readonly json?: boolean;
  /** Whether provider re-authentication is required. */
  readonly reauth_required?: boolean;
}
