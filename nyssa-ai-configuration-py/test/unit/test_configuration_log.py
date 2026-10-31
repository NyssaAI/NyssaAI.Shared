# Responsibility: Verify ConfigurationLog structured JSON output, debug gating, sink-failure containment, and allowlisted fields.
# Public interface:
# +-- test_configuration_log_contains_sink_failure_and_tracks_lost_records()
# +-- test_configuration_log_emits_structured_json_with_safe_context_only()
# `-- test_configuration_log_gates_debug_records_unless_enabled()
"""Unit tests for ConfigurationLog operational boundary logging (PY-LOG)."""

import json

from nyssa_ai_configuration.logging.configuration_log import ConfigurationLog


def test_configuration_log_emits_structured_json_with_safe_context_only() -> None:
    """Verify ConfigurationLog writes single-line JSON with allowlisted fields and monotonic elapsed_ms."""
    lines: list[str] = []
    logger = ConfigurationLog(sink=lines.append, debug_enabled=False)

    logger.degraded("file_lock.acquire", "lock.retry", 12.5, "filesystem", 2)
    logger.completed("store.set", "success", 3.5, dependency="filesystem", attempts=1)

    assert len(lines) == 2

    degraded_record = json.loads(lines[0])
    assert degraded_record["level"] == "warn"
    assert degraded_record["operation"] == "file_lock.acquire"
    assert degraded_record["code"] == "lock.retry"
    assert degraded_record["dependency"] == "filesystem"
    assert degraded_record["attempts"] == 2

    complete_record = json.loads(lines[1])
    assert complete_record["level"] == "info"
    assert complete_record["operation"] == "store.set"
    assert complete_record["outcome"] == "success"
    assert complete_record["elapsed_ms"] == 3.5


def test_configuration_log_gates_debug_records_unless_enabled() -> None:
    """Verify debug records are skipped when debug_enabled is False and emitted when True."""
    quiet_lines: list[str] = []
    quiet_logger = ConfigurationLog(sink=quiet_lines.append, debug_enabled=False)
    quiet_logger.debug("store.get", "cache.lookup", 0.5)
    assert quiet_lines == []

    verbose_lines: list[str] = []
    verbose_logger = ConfigurationLog(sink=verbose_lines.append, debug_enabled=True)
    verbose_logger.debug("store.get", "cache.lookup", 0.5)
    assert len(verbose_lines) == 1
    record = json.loads(verbose_lines[0])
    assert record["level"] == "debug"
    assert record["code"] == "cache.lookup"


def test_configuration_log_contains_sink_failure_and_tracks_lost_records() -> None:
    """Verify sink exceptions never mask primary operations and increment lost_records."""
    should_fail = True
    captured: list[str] = []

    def flaky_sink(line: str) -> None:
        if should_fail:
            raise OSError("disk full")
        captured.append(line)

    logger = ConfigurationLog(sink=flaky_sink)
    logger.completed("store.get", "success", 1.0)
    assert logger.lost_records == 1

    should_fail = False
    logger.completed("store.get", "success", 1.2)
    assert len(captured) == 1
    record = json.loads(captured[0])
    assert record["outcome"] == "success"
    assert logger.lost_records == 1
