# Responsibility: Unit test configuration validation, connection string password redaction, and standard logging integration.
# Public interface:
# +-- test_gateway_emits_to_standard_python_logging()
# +-- test_redact_connection_string_masks_secrets()
# +-- test_validate_configuration_invalid_raises()
# `-- test_validate_configuration_valid_passes()
"""Unit tests for configuration validation, redaction, and standard logging integration."""

import logging
import pytest
from nyssa_ai_dbgateway.types import DbGatewayConfiguration
from nyssa_ai_dbgateway.gateway.db_gateway_client import DbGatewayClient
from nyssa_ai_dbgateway.validate_db_gateway_configuration import validate_db_gateway_configuration
from nyssa_ai_dbgateway.logging.db_gateway_logger import redact_connection_string


def test_validate_configuration_valid_passes() -> None:
    config = DbGatewayConfiguration(
        database_provider="sqlite",
        connection_string="Data Source=:memory:",
        command_timeout_seconds=30,
    )
    validate_db_gateway_configuration(config)


def test_validate_configuration_invalid_raises() -> None:
    with pytest.raises(ValueError):
        validate_db_gateway_configuration(
            DbGatewayConfiguration(database_provider="", connection_string="test")
        )
    with pytest.raises(ValueError):
        validate_db_gateway_configuration(
            DbGatewayConfiguration(database_provider="sqlite", connection_string="")
        )
    with pytest.raises(ValueError):
        validate_db_gateway_configuration(
            DbGatewayConfiguration(database_provider="sqlite", connection_string="test", command_timeout_seconds=-1)
        )


def test_redact_connection_string_masks_secrets() -> None:
    raw = "Host=localhost;Port=5432;User ID=admin;Password=Secret123!;Database=mydb"
    redacted = redact_connection_string(raw)
    assert "Password=***" in redacted
    assert "Secret123!" not in redacted

    uri = "postgresql://user:SecretPass@localhost:5432/mydb"
    redacted_uri = redact_connection_string(uri)
    assert "://user:***@" in redacted_uri
    assert "SecretPass" not in redacted_uri


def test_gateway_emits_to_standard_python_logging() -> None:
    records: list[logging.LogRecord] = []

    class _CaptureHandler(logging.Handler):
        def emit(self, record: logging.LogRecord) -> None:
            records.append(record)

    pkg_logger = logging.getLogger("nyssa_ai_dbgateway")
    prev_level = pkg_logger.level
    handler = _CaptureHandler()
    pkg_logger.addHandler(handler)
    pkg_logger.setLevel(logging.DEBUG)
    try:
        client = DbGatewayClient.create(
            DbGatewayConfiguration(database_provider="sqlite", connection_string="Data Source=:memory:")
        )
        assert client.scalar("SELECT 42;") == 42
        assert len(records) == 1
        assert getattr(records[0], "operation", None) == "gateway.scalar"
        assert getattr(records[0], "outcome", None) == "success"
    finally:
        pkg_logger.removeHandler(handler)
        pkg_logger.setLevel(prev_level)

