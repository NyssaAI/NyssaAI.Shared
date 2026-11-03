// Responsibility: Request contract passed to a secret input provider.
// Public interface:
// `-- SecretPromptRequest
//     +-- availableProviders
//     +-- confirm
//     +-- defaultProvider
//     +-- key
//     +-- namespace
//     +-- promptMessage
//     `-- timeoutMs
export interface SecretPromptRequest {
  readonly namespace: string;
  readonly key: string;
  readonly promptMessage: string;
  readonly availableProviders: readonly string[];
  readonly defaultProvider: string;
  readonly confirm: boolean;
  readonly timeoutMs?: number | undefined;
}
