"""Use case tests demonstrating public Nyssa logging workflows mirroring C# and TS tutorials."""

from __future__ import annotations

import os

from nyssa_ai_logging.configuration.create_nyssa_logging import create_nyssa_logging
from nyssa_ai_logging.configuration.logging_builder import LoggingBuilder
from nyssa_ai_logging.logger import Logger
from test.unit.helpers.stderr_capture import StderrCapture
from test.unit.helpers.stdout_capture import StdoutCapture
from test.unit.helpers.temp_log_workspace import TempLogWorkspace


class OrderService:
    """Domain service injecting Logger via constructor dependency injection."""

    def __init__(self, logger: Logger) -> None:
        """Initialize with injected logger."""
        self._logger = logger

    def place(self, order_id: int) -> None:
        """Place an order and log operational outcome."""
        self._logger.info("Order {OrderId} placed", {"OrderId": order_id})


def test_write_logs_to_a_json_file() -> None:
    with TempLogWorkspace() as workspace:
        logging = LoggingBuilder("Information").add_nyssa_json_file(workspace.log_path)
        order_service = OrderService(logging.create_logger("OrderService"))

        order_service.place(1042)
        logging.dispose()

        records = workspace.read_records()
        assert len(records) == 1
        record = records[0]
        assert record["Level"] == "Information"
        assert record["Message"] == "Order 1042 placed"
        assert record["Module"] == "OrderService"
        assert record["AdditionalFields"]["OrderId"] == 1042


def test_configure_logging_in_one_call() -> None:
    with TempLogWorkspace() as workspace, StderrCapture() as stderr:
        logging = create_nyssa_logging(workspace.log_path, "Information")
        order_service = OrderService(logging.create_logger("OrderService"))

        order_service.place(1042)
        logging.dispose()

        assert "Order 1042 placed" in stderr.text
        records = workspace.read_records()
        assert len(records) == 1
        assert records[0]["Message"] == "Order 1042 placed"


def test_log_to_standard_error_only() -> None:
    with TempLogWorkspace() as workspace, StderrCapture() as stderr:
        logging = create_nyssa_logging(None, "Information")
        logger = logging.create_logger("LoggingTutorials")

        logger.info("Inventory checked for {Sku}", {"Sku": "ABC-123"})
        logger.debug("This debug message must be filtered out")
        logging.dispose()

        assert "Inventory checked for ABC-123" in stderr.text
        assert "This debug message" not in stderr.text
        assert not os.path.exists(workspace.log_path)


def test_chain_destinations() -> None:
    with TempLogWorkspace() as workspace, StderrCapture() as stderr, StdoutCapture() as stdout:
        logging = (
            create_nyssa_logging()
            .with_rotating_json_log(workspace.log_path)
            .with_stderr()
            .with_console()
        )
        logger = logging.create_logger("LoggingTutorials")
        logger.info("Order {OrderId} placed", {"OrderId": 1042})
        logging.dispose()

        assert "Order 1042 placed" in stdout.text
        assert "Order 1042 placed" in stderr.text

        directory = os.path.dirname(workspace.log_path)
        base_name = os.path.basename(workspace.log_path)
        dated_files = [f for f in os.listdir(directory) if f.endswith(f"-{base_name}")]
        assert len(dated_files) == 1

        records = workspace.read_records(os.path.join(directory, dated_files[0]))
        assert len(records) == 1
        assert records[0]["Message"] == "Order 1042 placed"


def test_log_structured_messages() -> None:
    with TempLogWorkspace() as workspace:
        logging = LoggingBuilder("Information").add_nyssa_json_file(workspace.log_path)
        logger = logging.create_logger("LoggingTutorials")

        logger.info(
            "Order {OrderId} accepted for {Amount}, shipping {Shipping} to {City}, {Region} {PostalCode}",
            {
                "OrderId": 1042,
                "Amount": 39.5,
                "Shipping": "Express",
                "City": "Seattle",
                "Region": "WA",
                "PostalCode": "98101",
            },
            operation="OrderAccepted",
        )
        logging.dispose()

        records = workspace.read_records()
        assert len(records) == 1
        record = records[0]

        assert record["Operation"] == "OrderAccepted"
        assert record["AdditionalFields"]["OrderId"] == 1042
        assert record["AdditionalFields"]["Amount"] == 39.5
        assert record["AdditionalFields"]["Shipping"] == "Express"
        assert record["AdditionalFields"]["City"] == "Seattle"
        assert record["AdditionalFields"]["PostalCode"] == "98101"


def test_choose_which_records_reach_the_file() -> None:
    with TempLogWorkspace() as workspace, StderrCapture() as stderr:
        logging = (
            LoggingBuilder("Information")
            .with_stderr()
            .add_nyssa_json_file(workspace.log_path, "Warning")
        )
        logger = logging.create_logger("LoggingTutorials")

        logger.info("Inventory checked for {Sku}", {"Sku": "ABC-123"})
        logger.warn("Inventory low for {Sku}", {"Sku": "ABC-123"})
        logging.dispose()

        assert "Inventory checked for ABC-123" in stderr.text
        assert "Inventory low for ABC-123" in stderr.text

        records = workspace.read_records()
        assert len(records) == 1
        assert records[0]["Message"] == "Inventory low for ABC-123"


def test_keep_running_when_the_log_file_cannot_be_opened() -> None:
    with TempLogWorkspace() as workspace, StderrCapture() as stderr:
        logging = (
            LoggingBuilder("Information")
            .add_nyssa_json_file("relative.log")
            .add_nyssa_json_file(workspace.log_path)
        )
        logger = logging.create_logger("LoggingTutorials")

        logger.warn("Still running")
        logging.dispose()

        assert "relative.log" in stderr.text
        assert "erro:" in stderr.text

        records = workspace.read_records()
        assert len(records) == 1
        assert records[0]["Message"] == "Still running"
