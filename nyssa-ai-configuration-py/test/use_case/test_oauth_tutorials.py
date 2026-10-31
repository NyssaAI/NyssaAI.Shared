# Responsibility: Exercise F-202 OAuth 2.0 PKCE loopback sign-in, 5-minute pre-expiry refresh, invalid_grant handling, and UC-004 in-memory grant durability.
# Public interface:
# +-- LoopbackBrowserSim
# |   +-- new()
# |   `-- open_url(url)
# +-- test_oauth_authorize_refresh_and_invalid_grant_marks_reauth_required(tmp_path)
# +-- test_oauth_rejects_non_https_endpoints(tmp_path)
# `-- test_oauth_uc004_retains_rotated_grant_in_memory_when_disk_write_fails(tmp_path)
"""Use-case tests for F-202 OAuth 2.0 PKCE authorization, token refresh, and held-grant durability."""

from collections.abc import Mapping
from datetime import UTC, datetime, timedelta
import json
from pathlib import Path
import urllib.parse
import urllib.request

from nyssa_ai_configuration import (
    BootstrapOptions,
    ConfigurationClient,
    KeyringInMemory,
    OAuthFlowClient,
    OAuthOptions,
    Result,
    fail,
    ok,
)


class LoopbackBrowserSim:
    """BrowserLauncher test helper that completes the local loopback OAuth callback."""

    def __init__(self) -> None:
        """Initialize with empty URL history."""
        self.launched_urls: list[str] = []

    def open_url(self, url: str) -> Result[None]:
        """Parse redirect_uri and state from url and trigger the local HTTP callback."""
        self.launched_urls.append(url)
        parsed = urllib.parse.urlparse(url)
        qs = urllib.parse.parse_qs(parsed.query)
        redirect_uri = qs["redirect_uri"][0]
        state = qs["state"][0]
        callback_url = f"{redirect_uri}?code=pkce-auth-code-001&state={urllib.parse.quote(state)}"
        with urllib.request.urlopen(callback_url, timeout=5) as resp:
            assert resp.status == 200
        return ok(None)


def test_oauth_rejects_non_https_endpoints(tmp_path: Path) -> None:
    """Verify authorize rejects http:// authorization or token endpoints with Validation."""
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(tmp_path / "store"),
            manifest_path=str(tmp_path / "config-manifest.json"),
        ),
        keyring=KeyringInMemory(),
    )
    assert created.ok
    client = created.value

    bad_opts = OAuthOptions(
        authorization_endpoint="http://insecure.example.com/authorize",
        token_endpoint="https://auth.example.com/token",
        client_id="client-1",
    )
    res = client.authorize("github", "token", bad_opts)
    assert not res.ok
    assert res.category == "Validation"


def test_oauth_authorize_refresh_and_invalid_grant_marks_reauth_required(tmp_path: Path) -> None:
    """Verify PKCE loopback sign-in, automatic refresh within 5 minutes of expiry, and reauth_required on invalid_grant."""
    store_dir = tmp_path / "store"
    current_time = datetime(2026, 10, 30, 12, 0, 0, tzinfo=UTC)
    should_revoke = False

    def fake_http_post(url: str, form: Mapping[str, str]) -> Result[tuple[int, str]]:
        del url
        grant_type = form.get("grant_type")
        if grant_type == "authorization_code":
            assert "code_verifier" in form
            return ok((200, json.dumps({"access_token": "acc-initial", "refresh_token": "ref-1", "expires_in": 600})))
        if should_revoke:
            return ok((400, json.dumps({"error": "invalid_grant"})))
        return ok((200, json.dumps({"access_token": "acc-refreshed", "refresh_token": "ref-2", "expires_in": 3600})))

    browser = LoopbackBrowserSim()
    flow_client = OAuthFlowClient(http_post=fake_http_post, now=lambda: current_time)
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(store_dir),
            manifest_path=str(tmp_path / "config-manifest.json"),
        ),
        keyring=KeyringInMemory(),
        oauth_flow_client=flow_client,
        browser_launcher=browser,
        clock=lambda: current_time.timestamp(),
    )
    assert created.ok
    client = created.value

    opts = OAuthOptions(
        authorization_endpoint="https://auth.nyssa.ai/authorize",
        token_endpoint="https://auth.nyssa.ai/token",
        client_id="nyssa-cli",
        scopes=("vault.read",),
    )
    assert client.authorize("auth", "session", opts).ok
    assert len(browser.launched_urls) == 1

    # 10 minutes before expiry: returns initial access token without refreshing
    first_read = client.get("auth", "session")
    assert first_read.ok
    assert first_read.value == "acc-initial"

    # Advance clock to 3 minutes before expiry (inside 5-minute refresh window)
    current_time = current_time + timedelta(minutes=7)
    refreshed_read = client.get("auth", "session")
    assert refreshed_read.ok
    assert refreshed_read.value == "acc-refreshed"

    # Advance clock again and simulate invalid_grant from OAuth server
    current_time = current_time + timedelta(minutes=58)
    should_revoke = True
    revoked_read = client.get("auth", "session")
    assert not revoked_read.ok
    assert revoked_read.category == "Forbidden"

    raw_doc = json.loads((store_dir / "auth.json").read_text(encoding="utf-8"))
    assert raw_doc["session"]["reauth_required"] is True

    # Reauthorize clears reauth_required and stores a fresh token
    should_revoke = False
    assert client.reauthorize("auth", "session").ok
    after_reauth = client.get("auth", "session")
    assert after_reauth.ok
    assert after_reauth.value == "acc-initial"


def test_oauth_uc004_retains_rotated_grant_in_memory_when_disk_write_fails(tmp_path: Path) -> None:
    """Verify UC-004: when refresh succeeds remotely but disk write fails, the rotated grant is held in memory and republished later."""
    store_dir = tmp_path / "store"
    current_time = datetime(2026, 10, 30, 12, 0, 0, tzinfo=UTC)
    refresh_calls = 0

    def fake_http_post(url: str, form: Mapping[str, str]) -> Result[tuple[int, str]]:
        nonlocal refresh_calls
        del url
        if form.get("grant_type") == "authorization_code":
            return ok((200, json.dumps({"access_token": "acc-v1", "refresh_token": "ref-v1", "expires_in": 120})))
        refresh_calls += 1
        if form.get("refresh_token") != "ref-v1":
            return ok((400, json.dumps({"error": "invalid_grant"})))
        return ok((200, json.dumps({"access_token": "acc-v2-rotated", "refresh_token": "ref-v2", "expires_in": 3600})))

    browser = LoopbackBrowserSim()
    flow_client = OAuthFlowClient(http_post=fake_http_post, now=lambda: current_time)
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(store_dir),
            manifest_path=str(tmp_path / "config-manifest.json"),
        ),
        keyring=KeyringInMemory(),
        oauth_flow_client=flow_client,
        browser_launcher=browser,
        clock=lambda: current_time.timestamp(),
    )
    assert created.ok
    client = created.value

    opts = OAuthOptions(
        authorization_endpoint="https://auth.nyssa.ai/authorize",
        token_endpoint="https://auth.nyssa.ai/token",
        client_id="nyssa-cli",
    )
    assert client.authorize("auth", "rotating", opts).ok

    # Inject a disk write failure during refresh
    def fail_disk_write(namespace: str) -> Result[None]:
        return fail(f"Simulated disk failure writing {namespace}.json", "Unavailable")

    client.set_document_write_interceptor(fail_disk_write)
    failed_refresh = client.get("auth", "rotating")
    assert not failed_refresh.ok
    assert refresh_calls == 1

    # Restore disk writes: next get() must republish the held rotated grant WITHOUT calling the token endpoint again!
    client.set_document_write_interceptor(None)
    recovered_read = client.get("auth", "rotating")
    assert recovered_read.ok
    assert recovered_read.value == "acc-v2-rotated"
    assert refresh_calls == 1
