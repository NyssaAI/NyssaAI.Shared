// Responsibility: Sanitized metadata receipt returned after a secret has been stored.
// Public interface:
// `-- SecretReceipt
//     +-- key
//     +-- namespace
//     +-- provider
//     `-- storedAtUtc
export interface SecretReceipt {
  readonly namespace: string;
  readonly key: string;
  readonly provider: string;
  readonly storedAtUtc: Date;
}
