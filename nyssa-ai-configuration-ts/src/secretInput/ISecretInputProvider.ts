// Responsibility: Contract for capturing secrets out-of-band directly from the user.
// Public interface:
// `-- ISecretInputProvider
//     `-- captureSecret(request, signal)
import { type Result } from "@nyssa-ai/result";
import type { CapturedSecret } from "./CapturedSecret.js";
import type { SecretPromptRequest } from "./SecretPromptRequest.js";

export interface ISecretInputProvider {
  captureSecret(request: SecretPromptRequest, signal?: AbortSignal): Promise<Result<CapturedSecret>>;
}
