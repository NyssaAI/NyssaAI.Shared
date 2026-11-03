// Responsibility: Public contract for capturing secrets out-of-band directly from the user.
// Public interface:
// `-- ConfigurationSecretWriter
//     `-- captureSecret(namespace, key, options, signal)
import { type Result } from "@nyssa-ai/result";
import type { CaptureSecretOptions } from "./CaptureSecretOptions.js";
import type { SecretReceipt } from "./SecretReceipt.js";

export interface ConfigurationSecretWriter {
  captureSecret(
    namespace: string,
    key: string,
    options?: CaptureSecretOptions,
    signal?: AbortSignal
  ): Promise<Result<SecretReceipt>>;
}
