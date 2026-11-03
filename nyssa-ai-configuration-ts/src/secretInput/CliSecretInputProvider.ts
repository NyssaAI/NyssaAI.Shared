// Responsibility: Masked terminal secret input provider with double-entry confirmation.
// Public interface:
// `-- CliSecretInputProvider
//     +-- captureSecret(request, signal)
//     `-- new(streamIn, streamOut)
import * as readline from "node:readline";
import { type Result, ok, fail } from "@nyssa-ai/result";
import { CapturedSecret } from "./CapturedSecret.js";
import type { ISecretInputProvider } from "./ISecretInputProvider.js";
import type { SecretPromptRequest } from "./SecretPromptRequest.js";

export class CliSecretInputProvider {
  private readonly _streamIn: NodeJS.ReadableStream;
  private readonly _streamOut: NodeJS.WritableStream;

  public constructor(streamIn?: NodeJS.ReadableStream, streamOut?: NodeJS.WritableStream) {
    this._streamIn = streamIn ?? process.stdin;
    this._streamOut = streamOut ?? process.stdout;
  }

  public async captureSecret(
    request: SecretPromptRequest,
    signal?: AbortSignal
  ): Promise<Result<CapturedSecret>> {
    signal?.throwIfAborted();

    try {
      this.write("\n");
      const title = request.promptMessage || `Enter secret for ${request.namespace}:${request.key}`;
      this.write(`${title} (${request.namespace}:${request.key})\n`);

      const isTTY = Boolean(
        Boolean((this._streamIn as { isTTY?: boolean }).isTTY) && typeof (this._streamIn as { setRawMode?: unknown }).setRawMode === "function"
      );

      if (!isTTY) {
        return await this.captureNonTty(request, signal);
      }

      return await this.captureTty(request, signal);
    } catch (err: unknown) {
      if (signal?.aborted) {
        return fail("Secret input was cancelled.", "Validation");
      }
      return fail(`Failed to capture secret from CLI: ${err}`, "Unexpected");
    }
  }

  private write(text: string): void {
    this._streamOut.write(text);
  }

  private async captureNonTty(
    request: SecretPromptRequest,
    signal?: AbortSignal
  ): Promise<Result<CapturedSecret>> {
    return new Promise((resolve) => {
      const rl = readline.createInterface({
        input: this._streamIn,
        output: this._streamOut,
        terminal: false
      });

      const onAbort = () => {
        rl.close();
        resolve(fail("Secret input was cancelled.", "Validation"));
      };
      signal?.addEventListener("abort", onAbort, { once: true });

      const lines: string[] = [];
      rl.on("line", (line) => {
        lines.push(line);
      });

      rl.on("close", () => {
        signal?.removeEventListener("abort", onAbort);
        let lineIdx = 0;
        let selectedProvider = request.defaultProvider;

        if (request.availableProviders.length > 1) {
          if (lineIdx < lines.length) {
            const choice = lines[lineIdx++]!.trim();
            const parsedIdx = parseInt(choice, 10);
            if (!isNaN(parsedIdx) && parsedIdx >= 1 && parsedIdx <= request.availableProviders.length) {
              selectedProvider = request.availableProviders[parsedIdx - 1]!;
            }
          }
        }

        if (lineIdx >= lines.length) {
          resolve(fail("No secret provided in input stream.", "Validation"));
          return;
        }

        const firstSecret = lines[lineIdx++]!;
        if (request.confirm) {
          if (lineIdx >= lines.length) {
            resolve(fail("Confirmation secret missing from input stream.", "Validation"));
            return;
          }
          const secondSecret = lines[lineIdx++]!;
          if (firstSecret !== secondSecret) {
            resolve(fail("The entered secrets do not match.", "Validation"));
            return;
          }
        }

        resolve(ok(new CapturedSecret(firstSecret, selectedProvider)));
      });

      rl.on("error", (err) => {
        signal?.removeEventListener("abort", onAbort);
        rl.close();
        resolve(fail(`CLI input error: ${err}`, "Unexpected"));
      });

      if (request.availableProviders.length > 1) {
        this.write("Select storage destination:\n");
        let defaultIdx = 1;
        for (let i = 0; i < request.availableProviders.length; i++) {
          const prov = request.availableProviders[i]!;
          const isDef = prov.toLowerCase() === request.defaultProvider.toLowerCase();
          if (isDef) {
            defaultIdx = i + 1;
          }
          const defStr = isDef ? " (Default)" : "";
          this.write(`  [${i + 1}] ${prov}${defStr}\n`);
        }
        this.write(`Destination [${defaultIdx}]: `);
      }
      this.write("Enter secret: ");
      if (request.confirm) {
        this.write("Confirm secret: ");
      }
    });
  }

  private async captureTty(
    request: SecretPromptRequest,
    signal?: AbortSignal
  ): Promise<Result<CapturedSecret>> {
    let selectedProvider = request.defaultProvider;

    if (request.availableProviders.length > 1) {
      this.write("Select storage destination:\n");
      let defaultIdx = 1;
      for (let i = 0; i < request.availableProviders.length; i++) {
        const prov = request.availableProviders[i]!;
        const isDef = prov.toLowerCase() === request.defaultProvider.toLowerCase();
        if (isDef) {
          defaultIdx = i + 1;
        }
        const defStr = isDef ? " (Default)" : "";
        this.write(`  [${i + 1}] ${prov}${defStr}\n`);
      }

      const choice = await this.readTtyLine(`Destination [${defaultIdx}]: `, signal);
      const parsedIdx = parseInt(choice.trim(), 10);
      if (!isNaN(parsedIdx) && parsedIdx >= 1 && parsedIdx <= request.availableProviders.length) {
        selectedProvider = request.availableProviders[parsedIdx - 1]!;
      }
    }

    const firstSecret = await this.readTtyMasked("Enter secret: ", signal);
    if (request.confirm) {
      const secondSecret = await this.readTtyMasked("Confirm secret: ", signal);
      if (firstSecret !== secondSecret) {
        return fail("The entered secrets do not match.", "Validation");
      }
    }

    return ok(new CapturedSecret(firstSecret, selectedProvider));
  }

  private readTtyLine(prompt: string, signal?: AbortSignal): Promise<string> {
    return new Promise((resolve, reject) => {
      const rl = readline.createInterface({
        input: this._streamIn,
        output: this._streamOut,
        terminal: true
      });

      const onAbort = () => {
        rl.close();
        reject(new Error("Aborted"));
      };
      signal?.addEventListener("abort", onAbort, { once: true });

      rl.question(prompt, (answer) => {
        signal?.removeEventListener("abort", onAbort);
        rl.close();
        resolve(answer);
      });
    });
  }

  private readTtyMasked(prompt: string, signal?: AbortSignal): Promise<string> {
    return new Promise((resolve, reject) => {
      if (signal?.aborted) {
        return reject(new Error("Aborted"));
      }

      const inStream = this._streamIn as NodeJS.ReadableStream & { setRawMode(mode: boolean): void; resume(): void; pause(): void };
      this.write(prompt);
      let password = "";
      inStream.setRawMode(true);
      inStream.resume();

      const onData = (chunk: Buffer) => {
        const str = chunk.toString("utf8");
        for (let i = 0; i < str.length; i++) {
          const char = str[i]!;
          if (char === "\r" || char === "\n") {
            cleanup();
            this.write("\n");
            resolve(password);
            return;
          } else if (char === "\u0003") {
            cleanup();
            reject(new Error("User cancelled"));
            return;
          } else if (char === "\u0008" || char === "\u007f") {
            if (password.length > 0) {
              password = password.slice(0, -1);
              this.write("\b \b");
            }
          } else {
            password += char;
            this.write("*");
          }
        }
      };

      const cleanup = () => {
        signal?.removeEventListener("abort", onAbort);
        inStream.removeListener("data", onData);
        inStream.setRawMode(false);
        inStream.pause();
      };

      const onAbort = () => {
        cleanup();
        reject(new Error("Aborted"));
      };
      signal?.addEventListener("abort", onAbort, { once: true });

      inStream.on("data", onData);
    });
  }
}
