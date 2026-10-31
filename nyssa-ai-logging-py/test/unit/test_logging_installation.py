"""Unit tests for create_nyssa_logging and LoggingBuilder."""

from __future__ import annotations

from datetime import UTC, datetime
import os

from nyssa_ai_logging.configuration.create_nyssa_logging import create_nyssa_logging
from nyssa_ai_logging.configuration.logging_builder import LoggingBuilder
from test.unit.helpers.fixed_clock import FixedClock
from test.unit.helpers.stderr_capture import StderrCapture
from test.unit.helpers.stdout_capture import StdoutCapture
from test.unit.helpers.temp_log_workspace import TempLogWorkspace


def test_installation_create_with_file_configures_both() -> None:
    with TempLogWorkspace() as workspace, StderrCapture() as stderr:
        logging = create_nyssa_logging(workspace.log_path, "Information")
        logger = logging.create_logger("OrderService")

        logger.info("Order {OrderId} placed", {"OrderId": 1042})
        logging.dispose()

        assert "Order 1042 placed" in stderr.text
        records = workspace.read_records()
        assert len(records) == 1
        assert records[0]["Message"] == "Order 1042 placed"
        assert records[0]["AdditionalFields"]["OrderId"] == 1042


def test_installation_with_rotating_json_log_prefixes_date() -> None:
    clock = FixedClock(datetime(2026, 10, 13, 12, 0, 0, tzinfo=UTC))
    with TempLogWorkspace() as workspace:
        app_log = os.path.join(workspace.directory_path, "app.json")
        logging = LoggingBuilder("Information", clock).with_rotating_json_log(app_log)
        logger = logging.create_logger("PaymentService")

        logger.info("Payment received")
        logging.dispose()

        expected_name = "2026.10.13-app.json"
        expected_path = os.path.join(workspace.directory_path, expected_name)
        assert os.path.exists(expected_path)

        records = workspace.read_records(expected_path)
        assert len(records) == 1
        assert records[0]["Message"] == "Payment received"


def test_installation_with_console_writes_to_stdout() -> None:
    with StdoutCapture() as stdout:
        logging = LoggingBuilder("Information").with_console()
        logger = logging.create_logger("CliCommand")

        logger.info("Task completed successfully")
        logging.dispose()

        assert "Task completed successfully" in stdout.text


def test_installation_minimum_level_filters_records() -> None:
    with StderrCapture() as stderr:
        logging = LoggingBuilder("Warning").with_stderr()
        logger = logging.create_logger("Worker")

        logger.debug("Debug event")
        logger.info("Info event")
        logger.warn("Warning event")
        logging.dispose()

        assert "Debug event" not in stderr.text
        assert "Info event" not in stderr.text
        assert "Warning event" in stderr.text
