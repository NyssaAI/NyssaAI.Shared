"""Use case tests demonstrating public Nyssa Python stdlib logging workflows and OpenTelemetry."""

from __future__ import annotations

from datetime import UTC, datetime
from enum import Enum
import logging
import os
import pytest

from nyssa_ai_logging import LoggingBuilder, configure_nyssa_logging, start_span
from test.unit.helpers.fixed_clock import FixedClock
from test.unit.helpers.stderr_capture import StderrCapture
from test.unit.helpers.stdout_capture import StdoutCapture
from test.unit.helpers.temp_log_workspace import TempLogWorkspace

logger = logging.getLogger(__name__)


class ShippingService(Enum):
    """Example enum formatted by name in structured fields."""

    Standard = 1
    Express = 2


class OrderService:
    """Domain service using standard Python module-level logging without logger injection."""

    def place(self, order_id: int) -> None:
        """Place an order and log operational outcome via stdlib logging."""
        logging.getLogger(f"{__name__}.OrderService").info(
            "Order {OrderId} placed",
            order_id,
            extra={"operation": "OrderPlaced"},
        )


def test_configure_logging_with_zero_arguments(monkeypatch: pytest.MonkeyPatch) -> None:
    clock = FixedClock(datetime(2026, 10, 13, 12, 0, 0, tzinfo=UTC))
    with TempLogWorkspace() as workspace, StderrCapture() as stderr:
        monkeypatch.chdir(workspace.directory_path)
        monkeypatch.setattr("sys.argv", ["orders_app.py"])

        with configure_nyssa_logging(clock=clock):
            order_service = OrderService()
            order_service.place(1042)

        assert "Order 1042 placed" in stderr.text
        expected_file = os.path.join(
            workspace.directory_path, "logs", "2026.10.13-orders_app-log.json"
        )
        records = workspace.read_records(expected_file)
        assert len(records) == 1
        record = records[0]
        assert record["Level"] == "Information"
        assert record["Message"] == "Order 1042 placed"
        assert record["Operation"] == "OrderPlaced"
        assert record["AdditionalFields"]["OrderId"] == 1042


def test_write_logs_to_a_json_file() -> None:
    with TempLogWorkspace() as workspace:
        with LoggingBuilder("Information").add_nyssa_json_file(workspace.log_path):
            order_service = OrderService()
            order_service.place(1042)

        records = workspace.read_records()
        assert len(records) == 1
        record = records[0]
        assert record["Level"] == "Information"
        assert record["Message"] == "Order 1042 placed"
        assert record["Module"].endswith("OrderService")
        assert record["AdditionalFields"]["OrderId"] == 1042


def test_log_to_standard_error_only() -> None:
    with TempLogWorkspace() as workspace, StderrCapture() as stderr:
        with configure_nyssa_logging(enable_file=False, minimum_level="Information"):
            log = logging.getLogger("LoggingTutorials")
            log.info("Inventory checked for {Sku}", {"Sku": "ABC-123"})
            log.debug("This debug message must be filtered out")

        assert "Inventory checked for ABC-123" in stderr.text
        assert "This debug message" not in stderr.text
        assert not os.path.exists(workspace.log_path)


def test_chain_destinations_and_opentelemetry() -> None:
    clock = FixedClock(datetime(2026, 10, 13, 12, 0, 0, tzinfo=UTC))
    with TempLogWorkspace() as workspace, StderrCapture() as stderr, StdoutCapture() as stdout:
        with (
            configure_nyssa_logging(
                app_name="checkout",
                log_dir=workspace.directory_path,
                enable_otel=True,
                clock=clock,
            )
            .with_stderr()
            .with_console()
        ) as telemetry:
            with start_span("PlaceOrder", {"Tenant": "acme"}) as span:
                logging.getLogger("LoggingTutorials").info("Order {OrderId} placed", 1042)

            otel_records = telemetry.otel_records

        assert "Order 1042 placed" in stdout.text
        assert "Order 1042 placed" in stderr.text
        assert len(otel_records) == 1
        assert otel_records[0].AdditionalFields["TraceId"] == span.trace_id

        dated_path = os.path.join(workspace.directory_path, "2026.10.13-checkout-log.json")
        records = workspace.read_records(dated_path)
        assert len(records) == 1
        assert records[0]["Message"] == "Order 1042 placed"
        assert records[0]["Operation"] == "PlaceOrder"
        assert records[0]["AdditionalFields"]["Tenant"] == "acme"
        assert records[0]["AdditionalFields"]["TraceId"] == span.trace_id


def test_log_structured_messages() -> None:
    with TempLogWorkspace() as workspace:
        with LoggingBuilder("Information").add_nyssa_json_file(workspace.log_path):
            logging.getLogger("LoggingTutorials").info(
                "Order {OrderId} accepted for {Amount}, shipping {Shipping} to {City}, {Region} {PostalCode}",
                1042,
                39.5,
                ShippingService.Express,
                "Seattle",
                "WA",
                "98101",
                extra={"operation": "OrderAccepted"},
            )

        records = workspace.read_records()
        assert len(records) == 1
        record = records[0]

        assert record["Operation"] == "OrderAccepted"
        assert record["AdditionalFields"]["OrderId"] == 1042
        assert record["AdditionalFields"]["Amount"] == 39.5
        assert record["AdditionalFields"]["Shipping"] == "Express"
        assert record["AdditionalFields"]["City"] == "Seattle"
        assert record["AdditionalFields"]["PostalCode"] == "98101"


def test_choose_which_records_reach_the_file_and_adjust_per_package() -> None:
    with TempLogWorkspace() as workspace, StderrCapture() as stderr:
        with (
            LoggingBuilder("Information")
            .with_stderr()
            .add_nyssa_json_file(workspace.log_path, "Warning")
            .set_package_level("inventory.audit", "Debug")
        ):
            logging.getLogger("inventory.service").info("Inventory checked for {Sku}", "ABC-123")
            logging.getLogger("inventory.audit. step").debug("Audit step completed")
            logging.getLogger("inventory.service").warning("Inventory low for {Sku}", "ABC-123")

        assert "Inventory checked for ABC-123" in stderr.text
        assert "Audit step completed" in stderr.text
        assert "Inventory low for ABC-123" in stderr.text

        records = workspace.read_records()
        assert len(records) == 1
        assert records[0]["Message"] == "Inventory low for ABC-123"


def test_keep_running_when_the_log_file_cannot_be_opened() -> None:
    with TempLogWorkspace() as workspace, StderrCapture() as stderr:
        with (
            LoggingBuilder("Information")
            .add_nyssa_json_file("relative.log")
            .add_nyssa_json_file(workspace.log_path)
        ):
            logging.getLogger("LoggingTutorials").warning("Still running")

        assert "relative.log" in stderr.text
        assert "erro:" in stderr.text

        records = workspace.read_records()
        assert len(records) == 1
        assert records[0]["Message"] == "Still running"
