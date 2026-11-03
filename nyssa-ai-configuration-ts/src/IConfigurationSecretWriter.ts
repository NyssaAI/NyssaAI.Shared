// Responsibility: Compatibility alias for the ConfigurationSecretWriter contract.
// Public interface:
// `-- IConfigurationSecretWriter
//     `-- captureSecret(namespace, key, options, signal)
import { type Result } from "@nyssa-ai/result";
import type { CaptureSecretOptions } from "./CaptureSecretOptions.js";
import type { SecretReceipt } from "./SecretReceipt.js";

/**
 * @deprecated Prefer `ConfigurationSecretWriter`.
 */
export interface IConfigurationSecretWriter {
  captureSecret(
    namespace: string,
    key: string,
    options?: CaptureSecretOptions,
    signal?: AbortSignal
  ): Promise<Result<SecretReceipt>>;
}
