// Responsibility: Ephemeral loopback HTTP secret input provider with session token.
// Public interface:
// `-- LoopbackSecretInputProvider
//     +-- cancel()
//     +-- captureSecret(request, signal)
//     +-- new(browserLauncher, streamOut)
//     +-- sessionToken
//     `-- waitForUrl(timeoutMs)
import * as http from "node:http";
import * as crypto from "node:crypto";
import { URL } from "node:url";
import { type Result, ok, fail } from "@nyssa-ai/result";
import type { IBrowserLauncher } from "../oauth/IBrowserLauncher.js";
import { CapturedSecret } from "./CapturedSecret.js";
import type { ISecretInputProvider } from "./ISecretInputProvider.js";
import type { SecretPromptRequest } from "./SecretPromptRequest.js";

export class LoopbackSecretInputProvider {
  private readonly _browserLauncher?: IBrowserLauncher | undefined;
  private readonly _streamOut: NodeJS.WritableStream;
  private readonly _sessionToken: string;
  private _server?: http.Server | undefined;
  private _boundUrl?: string | undefined;
  private _urlResolvers: Array<(url: string) => void> = [];
  private _cancelled: boolean = false;
  private _cancelHandler?: (() => void) | undefined;

  public constructor(browserLauncher?: IBrowserLauncher, streamOut?: NodeJS.WritableStream) {
    this._browserLauncher = browserLauncher;
    this._streamOut = streamOut ?? process.stdout;
    this._sessionToken = crypto.randomBytes(32).toString("hex");
  }

  public get sessionToken(): string {
    return this._sessionToken;
  }

  public async waitForUrl(timeoutMs: number = 5000): Promise<string | undefined> {
    if (this._boundUrl) {
      return this._boundUrl;
    }

    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        resolve(this._boundUrl);
      }, timeoutMs);

      this._urlResolvers.push((url) => {
        clearTimeout(timer);
        resolve(url);
      });
    });
  }

  public cancel(): void {
    this._cancelled = true;
    if (this._cancelHandler) {
      this._cancelHandler();
      this._cancelHandler = undefined;
    }
    if (this._server) {
      try {
        this._server.close();
      } catch {
        // ignore
      }
    }
  }

  public async captureSecret(
    request: SecretPromptRequest,
    signal?: AbortSignal
  ): Promise<Result<CapturedSecret>> {
    signal?.throwIfAborted();
    this._cancelled = false;

    return new Promise<Result<CapturedSecret>>((resolve) => {
      let settled = false;
      let timer: NodeJS.Timeout | undefined;
      const settle = (res: Result<CapturedSecret>) => {
        if (!settled) {
          settled = true;
          if (timer) clearTimeout(timer);
          this._cancelHandler = undefined;
          if (this._server) {
            try {
              this._server.close();
            } catch {
              // ignore
            }
          }
          resolve(res);
        }
      };
      this._cancelHandler = () => {
        settle(fail("Secret input was cancelled.", "Validation"));
      };
      if (request.timeoutMs && request.timeoutMs > 0) {
        timer = setTimeout(() => {
          settle(fail("Secret input timed out.", "Unavailable"));
        }, request.timeoutMs);
      }

      const onAbort = () => {
        this.cancel();
        settle(fail("Secret input was cancelled.", "Validation"));
      };
      signal?.addEventListener("abort", onAbort, { once: true });

      const server = http.createServer((req, res) => {
        const reqUrl = new URL(req.url ?? "/", `http://${req.headers.host ?? "127.0.0.1"}`);

        if (req.method === "GET") {
          const token = reqUrl.searchParams.get("token");
          if (token !== this._sessionToken) {
            res.writeHead(403, { "Content-Type": "text/plain" });
            res.end("Forbidden: Invalid session token.");
            return;
          }

          let optionsHtml = "";
          for (const p of request.availableProviders) {
            const isDef = p.toLowerCase() === request.defaultProvider.toLowerCase();
            const sel = isDef ? ' selected="selected"' : "";
            const label = this.formatProviderLabel(p);
            optionsHtml += `<option value="${this.escapeHtml(p)}"${sel}>${this.escapeHtml(label)}</option>`;
          }

          const confirmHtml = request.confirm
            ? `<div style="margin-bottom: 16px;">
                <label style="display:block; margin-bottom: 6px; font-weight: bold;">Confirm Secret</label>
                <input type="password" name="confirm_secret" required="required" style="width: 100%; box-sizing: border-box; padding: 8px; border: 1px solid #ccc; border-radius: 4px;" />
              </div>`
            : "";

          const fixedProv = request.availableProviders[0] ?? request.defaultProvider;
          const fixedLabel = this.formatProviderLabel(fixedProv);
          const destHtml =
            request.availableProviders.length === 1
              ? `<div style="margin-bottom: 16px;">
                  <label style="display:block; margin-bottom: 6px; font-weight: bold;">Storage Destination (Fixed by Application)</label>
                  <input type="text" value="${this.escapeHtml(fixedLabel)}" disabled="disabled" style="width: 100%; box-sizing: border-box; padding: 8px; border: 1px solid #ccc; border-radius: 4px; background: #e9ecef; color: #495057; font-size: 14px;" />
                  <input type="hidden" name="provider" value="${this.escapeHtml(fixedProv)}" />
                </div>`
              : `<div style="margin-bottom: 16px;">
                  <label style="display:block; margin-bottom: 6px; font-weight: bold;">Storage Destination</label>
                  <select name="provider" style="width: 100%; box-sizing: border-box; padding: 8px; border: 1px solid #ccc; border-radius: 4px; background: white; font-size: 14px;">
                    ${optionsHtml}
                  </select>
                  <small style="color: #666; display: block; margin-top: 4px;">Choose how to store the secret: free text (unencrypted), encrypted locally, or in a key vault.</small>
                </div>`;

          const title = request.promptMessage || `Enter Secret for ${request.namespace}:${request.key}`;

          const html = `<!DOCTYPE html>
<html>
<head><meta charset="utf-8" /><title>${this.escapeHtml(title)}</title></head>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background-color: #f4f6f8; display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0;">
  <div style="background: white; border: 1px solid #ddd; border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.1); padding: 32px; width: 440px; max-width: 90%;">
    <h2 style="margin-top: 0; color: #111;">Set Configuration Secret</h2>
    <p style="color: #666; font-size: 14px; margin-bottom: 24px;">Enter value for <code>${this.escapeHtml(request.namespace)}:${this.escapeHtml(request.key)}</code>. Plaintext is sent over local loopback directly to storage and never stored in LLM context.</p>
    <form method="POST" onsubmit="var s=document.querySelector('input[name=secret]').value; var c=document.querySelector('input[name=confirm_secret]'); if(c &amp;&amp; s!==c.value){ alert('Entered secrets do not match. Please verify.'); return false; }">
      <input type="hidden" name="session_token" value="${this.escapeHtml(this._sessionToken)}" />
      <div style="margin-bottom: 16px;">
        <label style="display:block; margin-bottom: 6px; font-weight: bold;">Secret Value</label>
        <input type="password" name="secret" autofocus="autofocus" required="required" style="width: 100%; box-sizing: border-box; padding: 8px; border: 1px solid #ccc; border-radius: 4px;" />
      </div>
      ${confirmHtml}
      ${destHtml}
      <button type="submit" style="width: 100%; background-color: #0066cc; color: white; border: none; padding: 10px; border-radius: 4px; font-weight: bold; cursor: pointer;">Save Secret</button>
    </form>
  </div>
</body>
</html>`;
          res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
          res.end(html);
        } else if (req.method === "POST") {
          let body = "";
          req.on("data", (chunk) => {
            body += chunk.toString();
          });
          req.on("end", () => {
            const params = new URLSearchParams(body);
            const token = params.get("session_token");
            if (token !== this._sessionToken) {
              res.writeHead(403, { "Content-Type": "text/plain" });
              res.end("Forbidden: Invalid session token.");
              return;
            }

            const secret = params.get("secret") ?? "";
            const confirmSecret = params.get("confirm_secret") ?? "";
            const provider = params.get("provider") ?? request.defaultProvider;

            if (request.confirm && secret !== confirmSecret) {
              res.writeHead(400, { "Content-Type": "text/plain" });
              res.end("Entered secrets do not match.");
              return;
            }

            res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
            res.end(`<!DOCTYPE html>
<html><head><meta charset="utf-8" /><title>Secret Saved</title></head>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background-color: #f4f6f8; display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0;">
  <div style="background: white; border: 1px solid #ddd; border-radius: 8px; padding: 32px; width: 440px; text-align: center;">
    <h2 style="color: #2e7d32; margin-top: 0;">&#10004; Secret Saved</h2>
    <p style="color: #555;">Secret captured securely and stored. You may close this tab.</p>
  </div>
</body></html>`);

            settle(ok(new CapturedSecret(secret, provider)));
          });
        } else {
          res.writeHead(405, { "Content-Type": "text/plain" });
          res.end("Method Not Allowed");
        }
      });

      this._server = server;

      server.listen(0, "127.0.0.1", () => {
        const addr = server.address();
        if (typeof addr === "object" && addr !== null) {
          const port = addr.port;
          const url = `http://127.0.0.1:${port}/?token=${this._sessionToken}`;
          this._boundUrl = url;
          for (const r of this._urlResolvers) {
            r(url);
          }
          this._urlResolvers = [];

          this._streamOut.write(`\nWaiting for secret input at:\n  ${url}\n\n`);

          if (this._browserLauncher) {
            this._browserLauncher.openUrl(url, signal).catch(() => {});
          }
        }
      });

      server.on("error", (err) => {
        settle(fail(`Loopback secret input server failed: ${err}`, "Unexpected"));
      });
    });
  }

  private formatProviderLabel(name: string): string {
    const lower = name.toLowerCase();
    if (lower === "aeslocal") return "Encrypted Locally (AES-256-GCM)";
    if (lower === "literal" || lower === "default" || lower === "plaintext" || lower === "") return "Free Text (Unencrypted Plaintext)";
    if (lower === "bws" || lower === "bitwarden" || lower === "bitwarden-secrets") return "Key Vault (Bitwarden Secrets Manager)";
    if (lower.startsWith("azure")) return "Key Vault (Azure Key Vault)";
    if (lower.startsWith("hashi") || lower === "vault") return "Key Vault (HashiCorp Vault)";
    return `Key Vault (${name})`;
  }

  private escapeHtml(str: string): string {
    return str
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#039;");
  }
}
