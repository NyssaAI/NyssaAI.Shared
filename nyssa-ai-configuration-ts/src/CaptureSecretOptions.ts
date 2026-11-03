// Responsibility: Configuration options for out-of-band secret capture.
// Public interface:
// `-- CaptureSecretOptions
//     +-- allowedProviders
//     +-- confirm
//     +-- inputProvider
//     +-- mode
//     +-- promptMessage
//     +-- targetProvider
//     `-- timeoutMs
import type { SecretCaptureMode } from "./SecretCaptureMode.js";
import type { ISecretInputProvider } from "./secretInput/ISecretInputProvider.js";

export interface CaptureSecretOptions {
  readonly promptMessage?: string | undefined;
  readonly targetProvider?: string | undefined;
  readonly allowedProviders?: readonly string[] | undefined;
  readonly confirm?: boolean | undefined;
  readonly mode?: SecretCaptureMode | undefined;
  readonly timeoutMs?: number | undefined;
  readonly inputProvider?: ISecretInputProvider | undefined;
}
