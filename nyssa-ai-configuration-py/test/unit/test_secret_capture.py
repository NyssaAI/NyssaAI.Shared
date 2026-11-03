from __future__ import annotations

import io
import re
import sys
import threading
import time
import typing
import urllib.parse
import urllib.request
from pathlib import Path

import pytest
from nyssa_ai_configuration.capture_secret_options import CaptureSecretOptions
from nyssa_ai_configuration.configuration_client import ConfigurationClient
from nyssa_ai_configuration.manifest.types import BootstrapOptions
from nyssa_ai_configuration.oauth.browser_launcher import BrowserLauncher
from nyssa_ai_configuration.oauth.oauth_flow_client import OauthFlowClient
from nyssa_ai_configuration.oauth.types import OAuthOptions
from nyssa_ai_configuration.secret_capture_mode import SecretCaptureMode
from nyssa_ai_configuration.secret_input.adaptive_secret_input_provider import (
    AdaptiveSecretInputProvider,
)
from nyssa_ai_configuration.secret_input.captured_secret import CapturedSecret
from nyssa_ai_configuration.secret_input.cli_secret_input_provider import (
    CliSecretInputProvider,
)
from nyssa_ai_configuration.secret_input.loopback_secret_input_provider import (
    LoopbackSecretInputProvider,
)
from nyssa_ai_configuration.secret_input.secret_input_provider import (
    SecretInputProvider,
)
from nyssa_ai_configuration.secret_input.secret_prompt_request import (
    SecretPromptRequest,
)
from nyssa_ai_configuration.secret_receipt import SecretReceipt
from nyssa_ai_result import Result, fail, ok


class MockSecretInputProvider(SecretInputProvider):
    def __init__(self, captured: CapturedSecret) -> None:
        self._captured = captured

    def capture(self, request: SecretPromptRequest) -> Result[CapturedSecret]:
        return ok(self._captured)


class FailingBrowserLauncher(BrowserLauncher):
    def open_url(self, url: str) -> Result[None]:
        return fail("Failed to open browser", "Unexpected")


def test_capture_secret_stores_encrypted_and_returns_receipt(tmp_path: Path) -> None:
    manifest_path = tmp_path / "manifest.json"
    config_dir = tmp_path / "config"
    config_dir.mkdir(parents=True)

    client_res = ConfigurationClient.create(
        BootstrapOptions(
            application_name="testapp",
            manifest_path=str(manifest_path),
            configuration_directory=str(config_dir),
        )
    )
    assert client_res.ok
    client = client_res.value

    mock_input = MockSecretInputProvider(CapturedSecret("s3cretP@ss", "aesLocal"))
    options = CaptureSecretOptions(
        prompt_message="Enter DB password",
        preferred_provider="aesLocal",
        input_provider=mock_input,
    )

    result = client.capture_secret("database", "password", options)
    assert result.ok
    receipt: SecretReceipt = result.value
    assert receipt.namespace == "database"
    assert receipt.key == "password"
    assert receipt.provider == "aesLocal"

    # Verify disk content is encrypted, not plaintext
    doc_text = (config_dir / "database.json").read_text("utf-8")
    assert "s3cretP@ss" not in doc_text
    assert '"provider": "aesLocal"' in doc_text
    assert '"encrypted": true' in doc_text

    # Verify normal get decrypts it
    read_res = client.get("database", "password")
    assert read_res.ok
    assert read_res.value == "s3cretP@ss"


def test_cli_secret_input_provider_double_entry_mismatch(monkeypatch: pytest.MonkeyPatch) -> None:
    out = io.StringIO()
    provider = CliSecretInputProvider(stream_out=out)

    monkeypatch.setattr("getpass.getpass", lambda prompt="", stream=None: "pass1" if "Confirm" not in prompt else "pass2")

    req = SecretPromptRequest(
        namespace="db",
        key="password",
        prompt_message="Enter password",
        available_providers=["aesLocal"],
        preferred_provider="aesLocal",
        confirm=True,
        timeout_seconds=5.0,
    )

    res = provider.capture(req)
    assert not res.ok
    assert res.error.category == "Validation"
    assert "match" in res.error.message.lower()


def test_cli_secret_input_provider_matching(monkeypatch: pytest.MonkeyPatch) -> None:
    out = io.StringIO()
    provider = CliSecretInputProvider(stream_out=out)

    monkeypatch.setattr("getpass.getpass", lambda prompt="", stream=None: "mySecureSecret")

    req = SecretPromptRequest(
        namespace="db",
        key="password",
        prompt_message="Enter password",
        available_providers=["aesLocal"],
        preferred_provider="aesLocal",
        confirm=True,
        timeout_seconds=5.0,
    )

    res = provider.capture(req)
    assert res.ok
    captured = res.value
    assert captured.value == "mySecureSecret"
    assert captured.selected_provider == "aesLocal"


def test_loopback_secret_input_provider_valid_post() -> None:
    out = io.StringIO()
    provider = LoopbackSecretInputProvider(stream_out=out)

    req = SecretPromptRequest(
        namespace="app",
        key="apiKey",
        prompt_message="Enter API Key",
        available_providers=["aesLocal"],
        preferred_provider="aesLocal",
        confirm=True,
        timeout_seconds=10.0,
    )

    captured_res: list[Result[CapturedSecret]] = []

    def _run_server() -> None:
        captured_res.append(provider.capture(req))

    t = threading.Thread(target=_run_server, daemon=True)
    t.start()

    url = provider.wait_for_url(timeout=5.0)
    assert url is not None
    assert "127.0.0.1" in url

    # POST valid form
    data = urllib.parse.urlencode({
        "session_token": provider.session_token,
        "secret": "superSecretToken",
        "confirm_secret": "superSecretToken",
        "provider": "aesLocal",
    }).encode("utf-8")

    req_http = urllib.request.Request(
        url,
        data=data,
        headers={"Content-Type": "application/x-www-form-urlencoded"},
        method="POST",
    )
    with urllib.request.urlopen(req_http, timeout=5) as resp:
        assert resp.status == 200

    t.join(timeout=5.0)
    assert len(captured_res) == 1
    assert captured_res[0].ok
    assert captured_res[0].value.value == "superSecretToken"


def test_loopback_secret_input_provider_invalid_token() -> None:
    out = io.StringIO()
    provider = LoopbackSecretInputProvider(stream_out=out)

    req = SecretPromptRequest(
        namespace="app",
        key="apiKey",
        prompt_message="Enter API Key",
        available_providers=["aesLocal"],
        preferred_provider="aesLocal",
        confirm=False,
        timeout_seconds=5.0,
    )

    captured_res: list[Result[CapturedSecret]] = []

    def _run_server() -> None:
        captured_res.append(provider.capture(req))

    t = threading.Thread(target=_run_server, daemon=True)
    t.start()

    url = provider.wait_for_url(timeout=5.0)
    assert url is not None

    data = urllib.parse.urlencode({
        "session_token": "wrong_token",
        "secret": "superSecretToken",
        "provider": "aesLocal",
    }).encode("utf-8")

    req_http = urllib.request.Request(
        url,
        data=data,
        headers={"Content-Type": "application/x-www-form-urlencoded"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req_http, timeout=5):
            pass
    except urllib.error.HTTPError as err:
        assert err.code == 403

    provider.cancel()
    t.join(timeout=5.0)


def test_adaptive_secret_input_provider() -> None:
    provider = AdaptiveSecretInputProvider(mode=SecretCaptureMode.CLI)
    assert provider is not None


def test_oauth_flow_client_prints_url_and_continues_if_browser_fails(capsys: pytest.CaptureFixture[str]) -> None:
    options = OAuthOptions(
        authorization_endpoint="https://auth.example.test/authorize",
        token_endpoint="https://auth.example.test/token",
        client_id="test-client",
        redirect_port=0,
    )

    failing_browser = FailingBrowserLauncher()

    def fake_post(url: str, form_data: typing.Mapping[str, str]) -> Result[tuple[int, str]]:
        return ok((200, '{"access_token": "acc-123", "refresh_token": "ref-456", "expires_in": 3600, "token_type": "Bearer"}'))

    client = OauthFlowClient(http_post=fake_post)

    auth_res: list[Result[typing.Any]] = []

    def _run_auth() -> None:
        auth_res.append(client.authorize(options, browser_launcher=failing_browser))

    t = threading.Thread(target=_run_auth, daemon=True)
    t.start()

    time.sleep(0.3)
    captured = capsys.readouterr()
    assert "https://auth.example.test/authorize" in captured.out
    assert "client_id=test-client" in captured.out

    # Find redirect_uri from printed URL to extract port and state
    m = re.search(r"redirect_uri=(http%3A%2F%2F127\.0\.0\.1%3A(\d+)%2Fcallback)", captured.out)
    assert m is not None
    port = int(m.group(2))

    m_state = re.search(r"state=([a-zA-Z0-9_-]+)", captured.out)
    assert m_state is not None
    state = m_state.group(1)

    # Hit the callback URL
    callback_url = f"http://127.0.0.1:{port}/callback?code=fake-code&state={state}"
    with urllib.request.urlopen(callback_url, timeout=5) as resp:
        assert resp.status == 200

    t.join(timeout=5.0)
    assert len(auth_res) == 1
    assert auth_res[0].ok
    assert auth_res[0].value.access_token == "acc-123"


def test_capture_secret_forced_provider(tmp_path: Path) -> None:
    manifest_path = tmp_path / "manifest.json"
    config_dir = tmp_path / "config"
    config_dir.mkdir(parents=True)

    client_res = ConfigurationClient.create(
        BootstrapOptions(
            application_name="testapp",
            manifest_path=str(manifest_path),
            configuration_directory=str(config_dir),
        )
    )
    assert client_res.ok
    client = client_res.value

    mock_input = MockSecretInputProvider(CapturedSecret("plain_secret", "literal"))
    options = CaptureSecretOptions(
        forced_provider="literal",
        input_provider=mock_input,
    )
    res = client.capture_secret("app", "api_key", options)
    assert res.ok
    assert res.value.provider == "literal"

    doc_text = (config_dir / "app.json").read_text("utf-8")
    assert "plain_secret" in doc_text

    invalid_opts = CaptureSecretOptions(forced_provider="nonexistent_vault", input_provider=mock_input)
    fail_res = client.capture_secret("app", "api_key2", invalid_opts)
    assert not fail_res.ok
    assert "not available" in fail_res.errors[0].technical_message


def test_capture_secret_unforced_defaults_to_aeslocal_but_allows_user_selection(tmp_path: Path) -> None:
    manifest_path = tmp_path / "manifest.json"
    config_dir = tmp_path / "config"
    config_dir.mkdir(parents=True)

    client_res = ConfigurationClient.create(
        BootstrapOptions(
            application_name="testapp",
            manifest_path=str(manifest_path),
            configuration_directory=str(config_dir),
        )
    )
    assert client_res.ok
    client = client_res.value

    captured_reqs: list[SecretPromptRequest] = []

    class InspectingProvider(SecretInputProvider):
        def capture(self, request: SecretPromptRequest) -> Result[CapturedSecret]:
            captured_reqs.append(request)
            return ok(CapturedSecret("user_val", "literal"))

    options = CaptureSecretOptions(input_provider=InspectingProvider())
    res = client.capture_secret("app", "user_choice", options)
    assert res.ok
    assert len(captured_reqs) == 1
    assert "aesLocal" in captured_reqs[0].available_providers
    assert "literal" in captured_reqs[0].available_providers
    assert captured_reqs[0].preferred_provider == "aesLocal"
    assert res.value.provider == "literal"
