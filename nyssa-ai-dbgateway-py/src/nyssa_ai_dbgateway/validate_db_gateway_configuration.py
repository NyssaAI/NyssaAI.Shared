# Responsibility: Pure validation function for DbGatewayConfiguration DTO.
# Public interface:
# `-- validate_db_gateway_configuration(config)
"""Validator for DbGatewayConfiguration."""

from nyssa_ai_dbgateway.types import DbGatewayConfiguration


def validate_db_gateway_configuration(config: DbGatewayConfiguration) -> None:
    """Validate settings fields without external resource access."""
    if not config.database_provider or not config.database_provider.strip():
        raise ValueError("DbGatewayConfiguration.database_provider must be a non-empty string.")
    if not config.connection_string or not config.connection_string.strip():
        raise ValueError("DbGatewayConfiguration.connection_string must be a non-empty string.")
    if config.command_timeout_seconds < 0:
        raise ValueError("DbGatewayConfiguration.command_timeout_seconds must be a non-negative integer.")
