# Responsibility: Capture secrets out-of-band using an ephemeral local HTTP server on 127.0.0.1 with session token.
# Public interface:
# `-- LoopbackSecretInputProvider
#     +-- cancel()
#     +-- capture(request)
#     +-- new(browser_launcher, stream_out)
#     +-- session_token
#     `-- wait_for_url(timeout)
"""Loopback HTTP server secret input provider."""

from __future__ import annotations

import html
from http.server import BaseHTTPRequestHandler, HTTPServer
import secrets
import sys
import threading
from typing import TextIO
import urllib.parse
import webbrowser

from nyssa_ai_configuration.oauth.browser_launcher import BrowserLauncher
from nyssa_ai_configuration.secret_input.captured_secret import CapturedSecret
from nyssa_ai_configuration.secret_input.secret_prompt_request import SecretPromptRequest
from nyssa_ai_result import Result, fail, ok


def _format_provider_label(name: str) -> str:
    lower = name.lower()
    if lower == "aeslocal":
        return "Encrypted Locally (AES-256-GCM)"
    if lower in ("literal", "default", "plaintext", ""):
        return "Free Text (Unencrypted Plaintext)"
    if lower in ("bws", "bitwarden", "bitwarden-secrets"):
        return "Key Vault (Bitwarden Secrets Manager)"
    if lower.startswith("azure"):
        return "Key Vault (Azure Key Vault)"
    if lower.startswith("hashi") or lower == "vault":
        return "Key Vault (HashiCorp Vault)"
    return f"Key Vault ({name})"


class LoopbackSecretInputProvider:
    """Ephemeral HTTP loopback server that presents a clean browser form to input the secret."""

    def __init__(
        self,
        browser_launcher: BrowserLauncher | None = None,
        stream_out: TextIO | None = None,
    ) -> None:
        self._browser_launcher = browser_launcher
        self._stream_out = stream_out or sys.stdout
        self._session_token = secrets.token_hex(32)
        self._server: HTTPServer | None = None
        self._url_event = threading.Event()
        self._bound_url: str | None = None
        self._cancel_event = threading.Event()

    @property
    def session_token(self) -> str:
        """Return the one-time session token."""
        return self._session_token

    def wait_for_url(self, timeout: float = 5.0) -> str | None:
        """Wait for the server to bind and return the local URL."""
        self._url_event.wait(timeout)
        return self._bound_url

    def cancel(self) -> None:
        """Cancel the pending capture."""
        self._cancel_event.set()
        if self._server:
            try:
                self._server.server_close()
            except Exception:
                pass

    def capture(self, request: SecretPromptRequest) -> Result[CapturedSecret]:
        """Start local HTTP server on ephemeral port, open browser, and await form POST."""
        captured: list[CapturedSecret] = []
        capture_error: list[str] = []
        done_event = threading.Event()
        session_token = self._session_token
        outer = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, format: str, *args: object) -> None:
                pass

            def do_GET(self) -> None:
                parsed = urllib.parse.urlparse(self.path)
                qs = urllib.parse.parse_qs(parsed.query)
                token = qs.get("token", [""])[0]
                if token != session_token:
                    self.send_response(403)
                    self.send_header("Content-Type", "text/plain")
                    self.end_headers()
                    self.wfile.write(b"Forbidden: Invalid session token.")
                    return

                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.end_headers()

                if len(request.available_providers) == 1:
                    fixed_prov = request.available_providers[0]
                    label = _format_provider_label(fixed_prov)
                    dest_html = f"""
                    <div style="margin-bottom: 16px;">
                        <label style="display:block; margin-bottom: 6px; font-weight: bold;">Storage Destination (Fixed by Application)</label>
                        <input type="text" value="{html.escape(label)}" disabled="disabled" style="width: 100%; box-sizing: border-box; padding: 8px; border: 1px solid #ccc; border-radius: 4px; background: #e9ecef; color: #495057; font-size: 14px;" />
                        <input type="hidden" name="provider" value="{html.escape(fixed_prov)}" />
                    </div>
                    """
                else:
                    options_html = ""
                    for p in request.available_providers:
                        sel = ' selected="selected"' if p.lower() == request.preferred_provider.lower() else ""
                        label = _format_provider_label(p)
                        options_html += f'<option value="{html.escape(p)}"{sel}>{html.escape(label)}</option>'

                    dest_html = f"""
                    <div style="margin-bottom: 16px;">
                        <label style="display:block; margin-bottom: 6px; font-weight: bold;">Storage Destination</label>
                        <select name="provider" style="width: 100%; box-sizing: border-box; padding: 8px; border: 1px solid #ccc; border-radius: 4px; background: white; font-size: 14px;">
                            {options_html}
                        </select>
                        <small style="color: #666; display: block; margin-top: 4px;">Choose how to store the secret: free text (unencrypted), encrypted locally, or in a key vault.</small>
                    </div>
                    """

                confirm_html = ""
                if request.confirm:
                    confirm_html = """
                    <div style="margin-bottom: 16px;">
                        <label style="display:block; margin-bottom: 6px; font-weight: bold;">Confirm Secret</label>
                        <input type="password" name="confirm_secret" required="required" style="width: 100%; box-sizing: border-box; padding: 8px; border: 1px solid #ccc; border-radius: 4px;" />
                    </div>
                    """

                title = request.prompt_message or f"Enter Secret for {request.namespace}:{request.key}"

                body = f"""<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <title>{html.escape(title)}</title>
</head>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background-color: #f4f6f8; display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0;">
    <div style="background: white; border: 1px solid #ddd; border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.1); padding: 32px; width: 440px; max-width: 90%;">
        <h2 style="margin-top: 0; color: #111;">Set Configuration Secret</h2>
        <p style="color: #666; font-size: 14px; margin-bottom: 24px;">Enter value for <code>{html.escape(request.namespace)}:{html.escape(request.key)}</code>. Plaintext is sent over local loopback directly to storage and never stored in LLM context.</p>
        <form method="POST" onsubmit="var s=document.querySelector('input[name=secret]').value; var c=document.querySelector('input[name=confirm_secret]'); if(c &amp;&amp; s!==c.value){{ alert('Entered secrets do not match. Please verify.'); return false; }}">
            <input type="hidden" name="session_token" value="{html.escape(session_token)}" />
            <div style="margin-bottom: 16px;">
                <label style="display:block; margin-bottom: 6px; font-weight: bold;">Secret Value</label>
                <input type="password" name="secret" autofocus="autofocus" required="required" style="width: 100%; box-sizing: border-box; padding: 8px; border: 1px solid #ccc; border-radius: 4px;" />
            </div>
            {confirm_html}
            {dest_html}
            <button type="submit" style="width: 100%; background-color: #0066cc; color: white; border: none; padding: 10px; border-radius: 4px; font-weight: bold; cursor: pointer;">Save Secret</button>
        </form>
    </div>
</body>
</html>"""
                self.wfile.write(body.encode("utf-8"))

            def do_POST(self) -> None:
                content_len = int(self.headers.get("Content-Length", 0))
                post_body = self.rfile.read(content_len).decode("utf-8")
                params = urllib.parse.parse_qs(post_body)

                token = params.get("session_token", [""])[0]
                if token != session_token:
                    self.send_response(403)
                    self.send_header("Content-Type", "text/plain")
                    self.end_headers()
                    self.wfile.write(b"Forbidden: Invalid session token.")
                    return

                secret = params.get("secret", [""])[0]
                confirm_secret = params.get("confirm_secret", [""])[0]
                provider = params.get("provider", [request.preferred_provider])[0]

                if request.confirm and secret != confirm_secret:
                    self.send_response(400)
                    self.send_header("Content-Type", "text/plain")
                    self.end_headers()
                    self.wfile.write(b"Entered secrets do not match.")
                    return

                captured.append(CapturedSecret(secret, provider))

                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.end_headers()
                self.wfile.write(b"""<!DOCTYPE html>
<html>
<head><meta charset="utf-8" /><title>Secret Saved</title></head>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background-color: #f4f6f8; display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0;">
    <div style="background: white; border: 1px solid #ddd; border-radius: 8px; padding: 32px; width: 440px; text-align: center;">
        <h2 style="color: #2e7d32; margin-top: 0;">&#10004; Secret Saved</h2>
        <p style="color: #555;">Secret captured securely and stored. You may close this tab.</p>
    </div>
</body>
</html>""")
                done_event.set()

        try:
            self._server = HTTPServer(("127.0.0.1", 0), Handler)
            port = self._server.server_address[1]
            url = f"http://127.0.0.1:{port}/?token={session_token}"
            self._bound_url = url
            self._url_event.set()

            self._stream_out.write(f"\nWaiting for secret input at:\n  {url}\n\n")
            self._stream_out.flush()

            if self._browser_launcher:
                self._browser_launcher.open_url(url)
            else:
                try:
                    webbrowser.open(url)
                except Exception:
                    pass

            while not done_event.is_set() and not self._cancel_event.is_set():
                self._server.timeout = 0.5
                self._server.handle_request()

            if captured:
                return ok(captured[0])
            if self._cancel_event.is_set():
                return fail("Secret input was cancelled.", "Validation")
            return fail("Secret input timed out.", "Unavailable")
        except Exception as exc:
            return fail(f"Loopback secret input failed: {exc}", "Unexpected", exc)
        finally:
            if self._server:
                try:
                    self._server.server_close()
                except Exception:
                    pass
