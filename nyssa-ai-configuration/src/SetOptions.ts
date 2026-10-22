// Responsibility: Options supplied when writing a single configuration entry.
// Public interface:
// `-- SetOptions
//     +-- encrypted
//     `-- provider

/**
 * SetOptions specifies optional encryption or provider routing when saving a configuration entry.
 */
export interface SetOptions {
  readonly encrypted?: boolean | undefined;
  readonly provider?: string | undefined;
}
