# Responsibility: Execute interactive OAuth 2.0 PKCE S256 loopback authorization and token refresh exchanges.
# Public interface:
# `-- OauthFlowClient
#     +-- authorize(options, browser_launcher)
#     +-- new(http_post, now)
#     +-- now
#     +-- refresh(token)
#     `-- validate_options(options)
"""OAuth 2.0 PKCE S256 loopback listener and token endpoint client."""

import base64
from collections.abc import Callable, Mapping
from datetime import UTC, datetime, timedelta
import hashlib
from http.server import BaseHTTPRequestHandler, HTTPServer
import json
import os
import threading
import urllib.error
import urllib.parse
import urllib.request
import webbrowser

from nyssa_ai_configuration.oauth.browser_launcher import BrowserLauncher
from nyssa_ai_configuration.oauth.types import OAuthOptions, OAuthToken
from nyssa_ai_configuration.results.result import Result, fail, ok


def _default_http_post(url: str, form_data: Mapping[str, str]) -> Result[tuple[int, str]]:
    encoded = urllib.parse.urlencode(form_data).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=encoded,
        headers={"Content-Type": "application/x-www-form-urlencoded", "Accept": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            return ok((int(resp.status), resp.read().decode("utf-8")))
    except urllib.error.HTTPError as http_err:
        return ok((int(http_err.code), http_err.read().decode("utf-8", errors="replace")))
    except Exception as exc:
        return fail(f"OAuth token endpoint request failed: {exc}", "Unavailable", exc)


class OauthFlowClient:
    """Runs interactive PKCE S256 loopback flows and non-interactive refresh token exchanges."""

    def __init__(
        self,
        http_post: Callable[[str, Mapping[str, str]], Result[tuple[int, str]]] = _default_http_post,
        now: Callable[[], datetime] = lambda: datetime.now(UTC),
    ) -> None:
        """Initialize with an HTTP form POST callable and UTC clock."""
        self._http_post = http_post
        self._now = now

    @property
    def now(self) -> Callable[[], datetime]:
        """Return the UTC clock callable."""
        return self._now

    @staticmethod
    def validate_options(options: OAuthOptions) -> Result[None]:
        """Validate that both OAuth endpoints use HTTPS and client_id is present."""
        if not options.client_id or not options.client_id.strip():
            return fail("OAuth client_id cannot be blank.", "Validation")
        for label, endpoint in (
            ("authorization_endpoint", options.authorization_endpoint),
            ("token_endpoint", options.token_endpoint),
        ):
            parsed = urllib.parse.urlparse(endpoint.strip())
            if parsed.scheme.lower() != "https" or not parsed.netloc:
                return fail(f"OAuth {label} '{endpoint}' must be a valid https:// URL.", "Validation")
        return ok(None)

    def authorize(
        self,
        options: OAuthOptions,
        browser_launcher: BrowserLauncher | None = None,
    ) -> Result[OAuthToken]:
        """Run an interactive PKCE S256 loopback authorization flow and exchange the code for an OAuthToken."""
        valid = self.validate_options(options)
        if not valid.ok:
            return valid

        verifier = base64.urlsafe_b64encode(os.urandom(32)).rstrip(b"=").decode("ascii")
        challenge = (
            base64.urlsafe_b64encode(hashlib.sha256(verifier.encode("ascii")).digest())
            .rstrip(b"=")
            .decode("ascii")
        )
        state = base64.urlsafe_b64encode(os.urandom(24)).rstrip(b"=").decode("ascii")
        captured: dict[str, str] = {}

        class _CallbackHandler(BaseHTTPRequestHandler):
            def do_GET(self) -> None:  # noqa: N802
                params = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)
                for k in ("error", "code", "state"):
                    if k in params:
                        captured[k] = params[k][0]
                self.send_response(200)
                self.send_header("Content-Type", "text/plain; charset=utf-8")
                self.end_headers()
                self.wfile.write(b"Authorization complete. You may close this window.")

            def log_message(self, format_str: str, *args: object) -> None:
                del format_str, args

        port = options.redirect_port if options.redirect_port is not None else 0
        try:
            server = HTTPServer(("127.0.0.1", port), _CallbackHandler)
        except Exception as exc:
            return fail(f"Failed to start local OAuth loopback listener: {exc}", "Unavailable", exc)

        redirect_uri = f"http://127.0.0.1:{int(server.server_port)}/callback"
        query_params: dict[str, str] = {
            "response_type": "code",
            "client_id": options.client_id,
            "redirect_uri": redirect_uri,
            "code_challenge": challenge,
            "code_challenge_method": "S256",
            "state": state,
        }
        if options.scopes:
            query_params["scope"] = " ".join(options.scopes)

        sep = "&" if "?" in options.authorization_endpoint else "?"
        auth_url = f"{options.authorization_endpoint}{sep}{urllib.parse.urlencode(query_params)}"

        server.timeout = 30.0
        thread = threading.Thread(target=server.handle_request, daemon=True)
        thread.start()
        try:
            if browser_launcher is not None:
                launch_res = browser_launcher.open_url(auth_url)
                if not launch_res.ok:
                    return launch_res
            else:
                webbrowser.open(auth_url)
            thread.join(timeout=30.0)
        finally:
            server.server_close()

        if "error" in captured:
            return fail(f"OAuth authorization denied: {captured['error']}", "Forbidden")
        if captured.get("state") != state:
            return fail("OAuth callback state mismatch or missing callback.", "Validation")
        code = captured.get("code")
        if not code:
            return fail("OAuth callback did not supply an authorization code.", "Validation")

        post_res = self._http_post(
            options.token_endpoint,
            {
                "grant_type": "authorization_code",
                "client_id": options.client_id,
                "code": code,
                "redirect_uri": redirect_uri,
                "code_verifier": verifier,
            },
        )
        if not post_res.ok:
            return post_res
        return self._parse_token_response(post_res.value[0], post_res.value[1], options, None)

    def refresh(self, token: OAuthToken) -> Result[OAuthToken]:
        """Exchange a refresh_token for a fresh OAuthToken without opening a browser."""
        options = OAuthOptions(
            authorization_endpoint=token.authorization_endpoint,
            token_endpoint=token.token_endpoint,
            client_id=token.client_id,
            scopes=token.scopes,
            redirect_port=token.redirect_port,
        )
        valid = self.validate_options(options)
        if not valid.ok:
            return valid

        post_res = self._http_post(
            token.token_endpoint,
            {
                "grant_type": "refresh_token",
                "client_id": token.client_id,
                "refresh_token": token.refresh_token,
            },
        )
        if not post_res.ok:
            return post_res
        return self._parse_token_response(post_res.value[0], post_res.value[1], options, token.refresh_token)

    def _parse_token_response(
        self,
        status: int,
        body: str,
        options: OAuthOptions,
        fallback_refresh_token: str | None,
    ) -> Result[OAuthToken]:
        try:
            payload: object = json.loads(body) if body.strip() else {}
        except Exception as exc:
            return fail("OAuth token endpoint returned malformed JSON.", "Unavailable", exc)

        if not isinstance(payload, dict):
            return fail("OAuth token endpoint response must be a JSON object.", "Unavailable")

        if status in (400, 401):
            err_code = str(payload.get("error", "invalid_grant"))
            if err_code == "invalid_grant" or status == 401:
                return fail(
                    "OAuth refresh grant was rejected (invalid_grant); re-authorization is required.",
                    "Forbidden",
                )
            return fail(f"OAuth token exchange rejected ({err_code}).", "Validation")
        if status < 200 or status >= 300:
            return fail(f"OAuth token endpoint failed with HTTP {status}.", "Unavailable")

        access_token = payload.get("access_token")
        if not isinstance(access_token, str) or not access_token.strip():
            return fail("OAuth token response missing 'access_token'.", "Unavailable")

        refresh_raw = payload.get("refresh_token")
        refresh_token = (
            refresh_raw
            if isinstance(refresh_raw, str) and refresh_raw.strip()
            else (fallback_refresh_token or "")
        )
        if not refresh_token:
            return fail("OAuth token response missing 'refresh_token'.", "Validation")

        expires_in_raw = payload.get("expires_in", 3600)
        expires_in = int(expires_in_raw) if isinstance(expires_in_raw, (int, float)) else 3600
        expires_at = (self._now() + timedelta(seconds=max(1, expires_in))).isoformat()

        return ok(
            OAuthToken(
                access_token=access_token,
                refresh_token=refresh_token,
                expires_at=expires_at,
                authorization_endpoint=options.authorization_endpoint,
                token_endpoint=options.token_endpoint,
                client_id=options.client_id,
                scopes=options.scopes,
                redirect_port=options.redirect_port,
            )
        )
