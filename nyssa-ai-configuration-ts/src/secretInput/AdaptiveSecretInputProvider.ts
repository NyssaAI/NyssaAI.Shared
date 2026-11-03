// Responsibility: Adaptively route between CLI masked input and Web loopback based on terminal TTY.
// Public interface:
// `-- AdaptiveSecretInputProvider
//     +-- captureSecret(request, signal)
//     `-- new(mode, browserLauncher, streamIn, streamOut)
import { type Result } from "@nyssa-ai/result";
import type { SecretCaptureMode } from "../SecretCaptureMode.js";
import type { IBrowserLauncher } from "../oauth/IBrowserLauncher.js";
import { CliSecretInputProvider } from "./CliSecretInputProvider.js";
import { LoopbackSecretInputProvider } from "./LoopbackSecretInputProvider.js";
import type { CapturedSecret } from "./CapturedSecret.js";
import type { ISecretInputProvider } from "./ISecretInputProvider.js";
import type { SecretPromptRequest } from "./SecretPromptRequest.js";

export class AdaptiveSecretInputProvider {
  private readonly _mode: SecretCaptureMode;
  private readonly _browserLauncher?: IBrowserLauncher | undefined;
  private readonly _streamIn?: NodeJS.ReadableStream | undefined;
  private readonly _streamOut?: NodeJS.WritableStream | undefined;

  public constructor(
    mode: SecretCaptureMode = "auto",
    browserLauncher?: IBrowserLauncher,
    streamIn?: NodeJS.ReadableStream,
    streamOut?: NodeJS.WritableStream
  ) {
    this._mode = mode;
    this._browserLauncher = browserLauncher;
    this._streamIn = streamIn;
    this._streamOut = streamOut;
  }

  public async captureSecret(
    request: SecretPromptRequest,
    signal?: AbortSignal
  ): Promise<Result<CapturedSecret>> {
    let resolvedMode = this._mode;
    if (resolvedMode === "auto") {
      const isTTY = Boolean(process.stdin.isTTY);
      resolvedMode = isTTY ? "cli" : "web";
    }

    if (resolvedMode === "cli") {
      const cli = new CliSecretInputProvider(this._streamIn, this._streamOut);
      return cli.captureSecret(request, signal);
    } else {
      const loopback = new LoopbackSecretInputProvider(this._browserLauncher, this._streamOut);
      return loopback.captureSecret(request, signal);
    }
  }
}
