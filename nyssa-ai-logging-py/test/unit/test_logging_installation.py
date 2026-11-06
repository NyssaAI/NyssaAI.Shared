"""Unit tests for configure_nyssa_logging, stdlib logging handlers, package levels, and OpenTelemetry spans."""

from __future__ import annotations

from datetime import UTC, datetime
import logging
import os
import pytest

from nyssa_ai_logging import (
    LoggingBuilder,
    configure_nyssa_logging,
    create_console_handler,
    create_json_file_handler,
    create_stderr_handler,
    start_span,
)
from test.unit.helpers.fixed_clock import FixedClock
from test.unit.helpers.stderr_capture import StderrCapture
from test.unit.helpers.stdout_capture import StdoutCapture
from test.unit.helpers.temp_log_workspace import TempLogWorkspace


def test_configure_nyssa_logging_zero_args_uses_safe_defaults(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    clock = FixedClock(datetime(2026, 10, 13, 12, 0, 0, tzinfo=UTC))
    with TempLogWorkspace() as workspace, StderrCapture() as stderr:
        monkeypatch.chdir(workspace.directory_path)
        monkeypatch.setattr("sys.argv", ["order_worker.py"])

        with configure_nyssa_logging(clock=clock):
            logger = logging.getLogger("orders.worker")
            logger.info("Order {OrderId} processed", 1042)

        expected_path = os.path.join(
            workspace.directory_path, "logs", "2026.10.13-order_worker-log.json"
        )
        assert os.path.exists(expected_path)
        assert "Order 1042 processed" in stderr.text

        records = workspace.read_records(expected_path)
        assert len(records) == 1
        assert records[0]["Message"] == "Order 1042 processed"
        assert records[0]["Module"] == "orders.worker"
        assert records[0]["AdditionalFields"]["OrderId"] == 1042


def test_configure_nyssa_logging_overrides_app_name_and_log_dir() -> None:
    clock = FixedClock(datetime(2026, 10, 13, 12, 0, 0, tzinfo=UTC))
    with TempLogWorkspace() as workspace, StderrCapture() as stderr:
        custom_dir = os.path.join(workspace.directory_path, "custom_logs")
        with configure_nyssa_logging(
            app_name="billing-service",
            log_dir=custom_dir,
            clock=clock,
        ):
            logging.getLogger("billing.api").info("Invoice {InvoiceId} issued", {"InvoiceId": 99})

        expected_path = os.path.join(custom_dir, "2026.10.13-billing-service-log.json")
        assert os.path.exists(expected_path)
        assert "Invoice 99 issued" in stderr.text

        records = workspace.read_records(expected_path)
        assert len(records) == 1
        assert records[0]["Message"] == "Invoice 99 issued"
        assert records[0]["AdditionalFields"]["InvoiceId"] == 99


def test_configure_nyssa_logging_with_explicit_file_path_writes_to_both() -> None:
    with TempLogWorkspace() as workspace, StderrCapture() as stderr:
        with configure_nyssa_logging(workspace.log_path, "Information"):
            logger = logging.getLogger("OrderService")
            logger.info("Order {OrderId} placed", {"OrderId": 1042})

        assert "Order 1042 placed" in stderr.text
        records = workspace.read_records()
        assert len(records) == 1
        assert records[0]["Message"] == "Order 1042 placed"
        assert records[0]["AdditionalFields"]["OrderId"] == 1042


def test_per_package_level_configuration_filters_and_enables_hierarchy() -> None:
    with TempLogWorkspace() as workspace, StderrCapture() as stderr:
        with configure_nyssa_logging(
            workspace.log_path,
            minimum_level="Information",
            package_levels={
                "a.b.c": "Debug",
                "noisy.third_party": "Error",
            },
        ):
            logging.getLogger("a.b.c.repository").debug("SQL query executed in 2ms")
            logging.getLogger("a.b.other").debug("Sibling debug filtered out")
            logging.getLogger("noisy.third_party.client").warning("Noisy warning suppressed")
            logging.getLogger("noisy.third_party.client").error("Critical third-party error")

        assert "SQL query executed in 2ms" in stderr.text
        assert "Sibling debug filtered out" not in stderr.text
        assert "Noisy warning suppressed" not in stderr.text
        assert "Critical third-party error" in stderr.text

        records = workspace.read_records()
        messages = [r["Message"] for r in records]
        assert messages == ["SQL query executed in 2ms", "Critical third-party error"]


def test_enable_otel_correlates_logs_with_programmatic_spans_and_records_exceptions() -> None:
    clock = FixedClock(datetime(2026, 10, 13, 12, 0, 0, tzinfo=UTC))
    with TempLogWorkspace() as workspace, StderrCapture():
        with configure_nyssa_logging(
            workspace.log_path,
            enable_otel=True,
            clock=clock,
        ) as telemetry:
            logger = logging.getLogger("checkout.workflow")

            with telemetry.start_span("CheckoutOrder", {"CartId": "cart-7"}) as parent_span:
                parent_span.set_attribute("Currency", "USD")
                logger.info("Starting checkout for {OrderId}", 1042)

                with start_span("ChargeCard", {"Provider": "Stripe"}) as child_span:
                    assert child_span.trace_id == parent_span.trace_id
                    assert child_span.parent_span_id == parent_span.span_id
                    logger.info("Card authorized")

            with pytest.raises(RuntimeError, match="gateway timeout"):
                with start_span("FailingStep") as error_span:
                    raise RuntimeError("gateway timeout")

            assert error_span.status_code == "ERROR"
            assert "RuntimeError: gateway timeout" in error_span.recorded_exceptions

            otel_captured = telemetry.otel_records

        records = workspace.read_records()
        assert len(records) == 2
        first, second = records[0], records[1]

        assert first["Operation"] == "CheckoutOrder"
        assert first["AdditionalFields"]["TraceId"] == parent_span.trace_id
        assert first["AdditionalFields"]["SpanId"] == parent_span.span_id
        assert first["AdditionalFields"]["CartId"] == "cart-7"
        assert first["AdditionalFields"]["Currency"] == "USD"
        assert first["AdditionalFields"]["OrderId"] == 1042

        assert second["Operation"] == "ChargeCard"
        assert second["AdditionalFields"]["TraceId"] == parent_span.trace_id
        assert second["AdditionalFields"]["SpanId"] == child_span.span_id
        assert second["AdditionalFields"]["ParentSpanId"] == parent_span.span_id
        assert second["AdditionalFields"]["Provider"] == "Stripe"

        assert len(otel_captured) == 2
        assert otel_captured[0].Message == "Starting checkout for 1042"


def test_manual_handler_configuration_works_with_standard_logging() -> None:
    with TempLogWorkspace() as workspace, StdoutCapture() as stdout, StderrCapture() as stderr:
        custom_logger = logging.getLogger("manual.custom.module")
        custom_logger.setLevel(logging.INFO)
        custom_logger.propagate = False

        file_handler = create_json_file_handler(workspace.log_path, "Warning")
        stderr_handler = create_stderr_handler("Information")
        console_handler = create_console_handler("Information")
        assert file_handler is not None

        custom_logger.addHandler(file_handler)
        custom_logger.addHandler(stderr_handler)
        custom_logger.addHandler(console_handler)
        try:
            custom_logger.info("Routine status %s", "ok", extra={"Status": "ok"})
            custom_logger.warning("Disk usage at %d%%", 92, extra={"DiskPct": 92})
        finally:
            custom_logger.removeHandler(file_handler)
            custom_logger.removeHandler(stderr_handler)
            custom_logger.removeHandler(console_handler)
            file_handler.close()
            stderr_handler.close()
            console_handler.close()

        assert "Routine status ok" in stdout.text
        assert "Routine status ok" in stderr.text
        records = workspace.read_records()
        assert len(records) == 1
        assert records[0]["Message"] == "Disk usage at 92%"
        assert records[0]["AdditionalFields"]["DiskPct"] == 92


def test_installation_minimum_level_filters_records() -> None:
    with StderrCapture() as stderr:
        with LoggingBuilder("Warning").with_stderr():
            logger = logging.getLogger("Worker")
            logger.debug("Debug event")
            logger.info("Info event")
            logger.warning("Warning event")

        assert "Debug event" not in stderr.text
        assert "Info event" not in stderr.text
        assert "Warning event" in stderr.text
