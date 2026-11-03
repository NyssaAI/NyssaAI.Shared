// Responsibility: Default loopback HTTP authorization code + PKCE flow client.
// Public interface:
// `-- OAuthLoopbackFlowClient
//     +-- authorizeCode(request, signal)
//     `-- refreshToken(request, signal)
import * as http from "node:http";
import { URL } from "node:url";
import { type Result, ok, fail } from "@nyssa-ai/result";
import type { IOAuthFlowClient } from "./IOAuthFlowClient.js";

export class OAuthLoopbackFlowClient {
  public async authorizeCode(
    request: {
      readonly authorizationEndpoint: string;
      readonly tokenEndpoint: string;
      readonly clientId: string;
      readonly scopes: ReadonlyArray<string>;
      readonly codeVerifier: string;
      readonly codeChallenge: string;
      readonly codeChallengeMethod: "S256";
      readonly state: string;
      readonly redirectPort?: number | undefined;
    },
    signal?: AbortSignal
  ): Promise<
    Result<{
      readonly accessToken: string;
      readonly refreshToken: string;
      readonly expiresInSeconds: number;
      readonly returnedState?: string | undefined;
    }>
  > {
    signal?.throwIfAborted();

    return new Promise((resolve) => {
      let settled = false;
      let server: http.Server | undefined;

      const cleanup = () => {
        if (server) {
          try {
            server.close();
          } catch {
            // ignore
          }
        }
      };

      const settle = (res: Result<{ readonly accessToken: string; readonly refreshToken: string; readonly expiresInSeconds: number; readonly returnedState?: string | undefined }>) => {
        if (!settled) {
          settled = true;
          cleanup();
          resolve(res);
        }
      };

      const onAbort = () => {
        settle(fail("OAuth authorization code flow was cancelled.", "Validation"));
      };
      signal?.addEventListener("abort", onAbort, { once: true });

      server = http.createServer(async (req, res) => {
        const reqUrl = new URL(req.url ?? "/", `http://${req.headers.host ?? "127.0.0.1"}`);

        if (reqUrl.pathname === "/callback") {
          const code = reqUrl.searchParams.get("code");
          const state = reqUrl.searchParams.get("state");
          const err = reqUrl.searchParams.get("error");

          if (err) {
            res.writeHead(400, { "Content-Type": "text/html; charset=utf-8" });
            res.end(`<html><body><h2>Authentication Failed</h2><p>${err}</p></body></html>`);
            settle(fail(`OAuth authorization server returned error: ${err}`, "Forbidden"));
            return;
          }

          if (!code) {
            res.writeHead(400, { "Content-Type": "text/plain" });
            res.end("Missing authorization code.");
            settle(fail("Missing code in OAuth callback.", "Validation"));
            return;
          }

          if (state !== request.state) {
            res.writeHead(400, { "Content-Type": "text/plain" });
            res.end("State mismatch.");
            settle(fail("OAuth state mismatch in callback.", "Validation"));
            return;
          }

          res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
          res.end(`<!DOCTYPE html>
<html><head><meta charset="utf-8" /><title>Authentication Successful</title></head>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background-color: #f4f6f8; display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0;">
  <div style="background: white; border: 1px solid #ddd; border-radius: 8px; padding: 32px; width: 440px; text-align: center;">
    <h2 style="color: #2e7d32; margin-top: 0;">&#10004; Authentication Successful</h2>
    <p style="color: #555;">You may close this tab and return to your terminal.</p>
  </div>
</body></html>`);

          const addr = server!.address(); const port = typeof addr === 'object' && addr !== null ? addr.port : 0; const redirectUri = `http://127.0.0.1:${port}/callback`;

          // Exchange authorization code for tokens
          try {
            const tokenRes = await this.exchangeCode(request.tokenEndpoint, {
              grant_type: "authorization_code",
              code,
              client_id: request.clientId,
              redirect_uri: redirectUri,
              code_verifier: request.codeVerifier
            }, signal);
            settle(tokenRes);
          } catch (exchangeErr: unknown) {
            settle(fail(`Token exchange failed: ${exchangeErr}`, "Unavailable"));
          }
        } else {
          res.writeHead(404, { "Content-Type": "text/plain" });
          res.end("Not Found");
        }
      });

      const port = request.redirectPort ?? 0;
      server.listen(port, "127.0.0.1", () => {});
      server.on("error", (err) => {
        settle(fail(`Failed to bind loopback server for OAuth callback: ${err}`, "Unavailable"));
      });
    });
  }

  public async refreshToken(
    request: {
      readonly tokenEndpoint: string;
      readonly clientId: string;
      readonly refreshToken: string;
      readonly scopes: ReadonlyArray<string>;
    },
    signal?: AbortSignal
  ): Promise<
    Result<{
      readonly accessToken: string;
      readonly refreshToken?: string | undefined;
      readonly expiresInSeconds: number;
    }>
  > {
    signal?.throwIfAborted();

    return this.exchangeCode(request.tokenEndpoint, {
      grant_type: "refresh_token",
      refresh_token: request.refreshToken,
      client_id: request.clientId,
      scope: request.scopes.join(" ")
    }, signal);
  }

  private async exchangeCode(
    tokenEndpoint: string,
    params: Record<string, string>,
    signal?: AbortSignal
  ): Promise<Result<{ readonly accessToken: string; readonly refreshToken: string; readonly expiresInSeconds: number }>> {
    try {
      const body = new URLSearchParams(params).toString();
      const reqInit: RequestInit = {
        method: "POST",
        headers: {
          "Content-Type": "application/x-www-form-urlencoded"
        },
        body
      };
      if (signal) {
        reqInit.signal = signal;
      }
      const response = await fetch(tokenEndpoint, reqInit);

      if (!response.ok) {
        const text = await response.text();
        return fail(`Token endpoint returned ${response.status}: ${text}`, "Forbidden");
      }

      const json = (await response.json()) as Record<string, unknown>;
      if (!json.access_token) {
        return fail("Token response missing access_token", "Validation");
      }

      return ok({
        accessToken: String(json["access_token"] ?? ""),
        refreshToken: String(json["refresh_token"] ?? params["refresh_token"] ?? ""),
        expiresInSeconds: typeof json["expires_in"] === "number" ? json["expires_in"] : 3600
      });
    } catch (err: unknown) {
      if (signal?.aborted) {
        return fail("Token request was aborted.", "Validation");
      }
      return fail(`Failed to request token from endpoint: ${err}`, "Unavailable");
    }
  }
}
