# Responsibility: Manage sealed 'oauth' entries, 5-minute pre-expiry refresh under lock, and in-memory rotated grant durability.
# Public interface:
# `-- OauthTokenProvider
#     +-- authorize(namespace, key, options, document, browser_launcher)
#     +-- clear_held(namespace, key)
#     +-- decode_token(raw_json)
#     +-- encode_token(token)
#     +-- new(cipher, flow_client, browser_launcher, now, logger, clock)
#     +-- read_access_token(namespace, key, entry, document)
#     `-- reauthorize(namespace, key, document, browser_launcher)
"""OAuth token storage provider implementing F-202 refresh and held-grant durability."""

from collections.abc import Callable
from datetime import UTC, datetime, timedelta
import json
import time
from typing import Final

from nyssa_ai_configuration.encryption.configuration_cipher import ConfigurationCipher
from nyssa_ai_configuration.logging.configuration_logger import ConfigurationLogger
from nyssa_ai_configuration.oauth.browser_launcher import BrowserLauncher
from nyssa_ai_configuration.oauth.oauth_flow_client import OauthFlowClient
from nyssa_ai_configuration.oauth.types import OAuthHeldGrant, OAuthOptions, OAuthToken
from nyssa_ai_configuration.results.result import Result, fail, ok
from nyssa_ai_configuration.values.configuration_document_local_file import (
    ConfigurationDocumentLocalFile,
)
from nyssa_ai_configuration.values.types import ConfigurationValue

_REFRESH_WINDOW: Final[timedelta] = timedelta(minutes=5)


class OauthTokenProvider:
    """Seals OAuth grants, refreshes expiring tokens under lock, and retains rotated grants in memory on disk errors."""

    def __init__(
        self,
        cipher: ConfigurationCipher | None,
        flow_client: OauthFlowClient | None = None,
        browser_launcher: BrowserLauncher | None = None,
        now: Callable[[], datetime] | None = None,
        logger: ConfigurationLogger | None = None,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        """Initialize with cipher, OAuth flow client, optional browser launcher, UTC clock, and logger."""
        self._cipher = cipher
        self._flow_client = flow_client or OauthFlowClient(now=now or (lambda: datetime.now(UTC)))
        self._browser_launcher = browser_launcher
        self._now = now or self._flow_client.now
        self._logger = logger
        self._clock = clock
        self._held_grants: dict[str, OAuthHeldGrant] = {}

    @staticmethod
    def encode_token(token: OAuthToken) -> str:
        """Serialize an OAuthToken into JSON matching .NET OAuthGrantJsonContext."""
        data: dict[str, object] = {
            "AccessToken": token.access_token,
            "RefreshToken": token.refresh_token,
            "TokenType": token.token_type,
            "ExpiresAt": token.expires_at,
            "AuthorizationEndpoint": token.authorization_endpoint,
            "TokenEndpoint": token.token_endpoint,
            "ClientId": token.client_id,
            "Scopes": list(token.scopes),
            "RedirectPort": token.redirect_port,
        }
        return json.dumps(data, indent=2)

    @staticmethod
    def decode_token(raw_json: str) -> Result[OAuthToken]:
        """Deserialize an OAuthToken from .NET PascalCase or snake_case JSON."""
        try:
            parsed: object = json.loads(raw_json)
        except Exception as exc:
            return fail(f"Stored OAuth token payload is not valid JSON: {exc}", "Validation", exc)
        if not isinstance(parsed, dict):
            return fail("Stored OAuth token payload must be a JSON object.", "Validation")
        access = parsed.get("AccessToken", parsed.get("access_token"))
        refresh = parsed.get("RefreshToken", parsed.get("refresh_token"))
        expires = parsed.get("ExpiresAt", parsed.get("expires_at"))
        auth_ep = parsed.get("AuthorizationEndpoint", parsed.get("authorization_endpoint"))
        tok_ep = parsed.get("TokenEndpoint", parsed.get("token_endpoint"))
        cid = parsed.get("ClientId", parsed.get("client_id"))
        tok_type = parsed.get("TokenType", parsed.get("token_type", "Bearer"))
        if not all(isinstance(v, str) for v in (access, refresh, expires, auth_ep, tok_ep, cid)):
            return fail("Stored OAuth token payload is missing required string fields.", "Validation")
        scopes_raw = parsed.get("Scopes", parsed.get("scopes", []))
        scopes = tuple(str(s) for s in scopes_raw) if isinstance(scopes_raw, list) else ()
        port_raw = parsed.get("RedirectPort", parsed.get("redirect_port"))
        port = int(port_raw) if isinstance(port_raw, int) and not isinstance(port_raw, bool) else None
        return ok(OAuthToken(
            access_token=str(access), refresh_token=str(refresh), expires_at=str(expires),
            authorization_endpoint=str(auth_ep), token_endpoint=str(tok_ep), client_id=str(cid),
            scopes=scopes, redirect_port=port, token_type=str(tok_type) if isinstance(tok_type, str) else "Bearer",
        ))

    def clear_held(self, namespace: str, key: str) -> None:
        """Remove any held in-memory grant for namespace/key when an entry is removed or overwritten."""
        self._held_grants.pop(f"{namespace}/{key}", None)

    def authorize(
        self,
        namespace: str,
        key: str,
        options: OAuthOptions,
        document: ConfigurationDocumentLocalFile,
        browser_launcher: BrowserLauncher | None = None,
    ) -> Result[None]:
        """Run interactive PKCE authorization and store the sealed OAuthToken under (namespace, key)."""
        if self._cipher is None:
            return fail("An encryption cipher is required to store OAuth tokens.", "Unavailable")
        valid_opts = OauthFlowClient.validate_options(options)
        if not valid_opts.ok:
            return valid_opts

        token_res = self._flow_client.authorize(options, browser_launcher or self._browser_launcher)
        if not token_res.ok:
            return token_res

        sealed = self._cipher.seal(self.encode_token(token_res.value), namespace, key)
        if not sealed.ok:
            return sealed

        new_entry = ConfigurationValue(value=sealed.value, provider="oauth", encrypted=True, reauth_required=False)

        def _save(entries: dict[str, ConfigurationValue]) -> Result[bool]:
            existing = entries.get(key)
            if existing is not None and existing.provider not in ("", "oauth"):
                return fail(
                    f"Key '{namespace}/{key}' is already stored in provider '{existing.provider}'; use move first.",
                    "Conflict",
                )
            entries[key] = new_entry
            return ok(True)

        mod_res = document.modify(_save)
        if not mod_res.ok:
            return mod_res
        self.clear_held(namespace, key)
        return ok(None)

    def reauthorize(
        self,
        namespace: str,
        key: str,
        document: ConfigurationDocumentLocalFile,
        browser_launcher: BrowserLauncher | None = None,
    ) -> Result[None]:
        """Read stored OAuth endpoint metadata from an existing 'oauth' entry and run a fresh interactive PKCE flow."""
        if self._cipher is None:
            return fail("An encryption cipher is required to reauthorize OAuth tokens.", "Unavailable")
        read_res = document.read()
        if not read_res.ok:
            return read_res
        entry = read_res.value.get(key)
        if entry is None:
            return fail(f"No OAuth entry found at '{namespace}/{key}'.", "NotFound")
        if entry.provider != "oauth":
            return fail(f"Entry '{namespace}/{key}' is not managed by the 'oauth' provider.", "Validation")

        opened = self._cipher.open(entry.value, namespace, key)
        if not opened.ok:
            return opened
        decoded = self.decode_token(opened.value)
        if not decoded.ok:
            return decoded
        tok = decoded.value
        options = OAuthOptions(
            authorization_endpoint=tok.authorization_endpoint,
            token_endpoint=tok.token_endpoint,
            client_id=tok.client_id,
            scopes=tok.scopes,
            redirect_port=tok.redirect_port,
        )
        return self.authorize(namespace, key, options, document, browser_launcher)

    def read_access_token(
        self,
        namespace: str,
        key: str,
        entry: ConfigurationValue,
        document: ConfigurationDocumentLocalFile,
    ) -> Result[str]:
        """Return a valid bearer access token, refreshing within 5 minutes of expiry or republishing held grants."""
        if self._cipher is None:
            return fail("An encryption cipher is required to read OAuth tokens.", "Unavailable")

        address = f"{namespace}/{key}"
        held = self._held_grants.get(address)
        if held is not None:
            persisted = self._persist_held(namespace, key, held, document)
            return ok(held.token.access_token) if persisted.ok else persisted

        if entry.reauth_required:
            return fail(
                f"OAuth credential '{namespace}/{key}' requires interactive re-authorization.",
                "Forbidden",
            )

        opened = self._cipher.open(entry.value, namespace, key)
        if not opened.ok:
            return opened
        decoded = self.decode_token(opened.value)
        if not decoded.ok:
            return decoded
        token = decoded.value

        if not self._needs_refresh(token):
            return ok(token.access_token)

        return self._refresh_under_lock(namespace, key, entry, document)

    def _needs_refresh(self, token: OAuthToken) -> bool:
        try:
            exp = datetime.fromisoformat(token.expires_at)
            if exp.tzinfo is None:
                exp = exp.replace(tzinfo=UTC)
            return exp <= (self._now() + _REFRESH_WINDOW)
        except ValueError:
            return True

    def _refresh_under_lock(
        self,
        namespace: str,
        key: str,
        initial_entry: ConfigurationValue,
        document: ConfigurationDocumentLocalFile,
    ) -> Result[str]:
        assert self._cipher is not None
        started = self._clock()
        refreshed_token_box: list[OAuthToken] = []
        sealed_entry_box: list[ConfigurationValue] = []
        reauth_error_box: list[Result[str]] = []

        def _mutator(entries: dict[str, ConfigurationValue]) -> Result[bool]:
            assert self._cipher is not None
            current = entries.get(key)
            if current is None or current.provider != "oauth":
                return fail(f"OAuth entry '{namespace}/{key}' was removed or changed.", "Conflict")
            if current.reauth_required:
                reauth_error_box.append(
                    fail(f"OAuth credential '{namespace}/{key}' requires interactive re-authorization.", "Forbidden")
                )
                return ok(False)

            opened_cur = self._cipher.open(current.value, namespace, key)
            if not opened_cur.ok:
                return opened_cur
            decoded_cur = self.decode_token(opened_cur.value)
            if not decoded_cur.ok:
                return decoded_cur
            cur_token = decoded_cur.value

            if not self._needs_refresh(cur_token):
                refreshed_token_box.append(cur_token)
                return ok(False)

            ref_res = self._flow_client.refresh(cur_token)
            if not ref_res.ok:
                if ref_res.category == "Forbidden":
                    entries[key] = ConfigurationValue(
                        value=current.value,
                        provider="oauth",
                        encrypted=True,
                        reauth_required=True,
                    )
                    reauth_error_box.append(ref_res)
                    return ok(True)
                return ref_res

            new_tok = ref_res.value
            sealed_new = self._cipher.seal(self.encode_token(new_tok), namespace, key)
            if not sealed_new.ok:
                return sealed_new
            new_entry = ConfigurationValue(
                value=sealed_new.value,
                provider="oauth",
                encrypted=True,
                reauth_required=False,
            )
            refreshed_token_box.append(new_tok)
            sealed_entry_box.append(new_entry)
            entries[key] = new_entry
            return ok(True)

        mod_res = document.modify(_mutator)
        if reauth_error_box:
            return reauth_error_box[0]

        if not mod_res.ok:
            if refreshed_token_box and sealed_entry_box:
                held = OAuthHeldGrant(
                    token=refreshed_token_box[0],
                    sealed_ciphertext=sealed_entry_box[0].value,
                    original_ciphertext=initial_entry.value,
                )
                for _ in range(2):
                    retry_res = self._persist_held(namespace, key, held, document)
                    if retry_res.ok:
                        return ok(held.token.access_token)
                    if retry_res.category == "Conflict":
                        return retry_res
                self._held_grants[f"{namespace}/{key}"] = held
                if self._logger is not None:
                    elapsed_ms = (self._clock() - started) * 1000.0
                    self._logger.degraded("oauth.refresh", "oauth.held_in_memory", elapsed_ms, "filesystem", 3)
            return mod_res

        return ok(refreshed_token_box[0].access_token)

    def _persist_held(
        self,
        namespace: str,
        key: str,
        held: OAuthHeldGrant,
        document: ConfigurationDocumentLocalFile,
    ) -> Result[None]:
        address = f"{namespace}/{key}"

        def _write_held(entries: dict[str, ConfigurationValue]) -> Result[bool]:
            current = entries.get(key)
            if (
                current is None
                or current.provider != "oauth"
                or current.value not in (held.original_ciphertext, held.sealed_ciphertext)
            ):
                self._held_grants.pop(address, None)
                return fail(f"OAuth entry '{address}' was modified or removed concurrently.", "Conflict")
            entries[key] = ConfigurationValue(
                value=held.sealed_ciphertext,
                provider="oauth",
                encrypted=True,
                reauth_required=False,
            )
            return ok(True)

        res = document.modify(_write_held)
        if res.ok:
            self._held_grants.pop(address, None)
            return ok(None)
        return res
