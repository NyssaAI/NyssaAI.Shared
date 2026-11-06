# Responsibility: Represent an active or no-op OpenTelemetry span scope for programmatic instrumentation.
# Public interface:
# `-- SpanScope
#     +-- attributes
#     +-- is_recording
#     +-- name
#     +-- new(name, trace_id, span_id, parent_span_id, operation, attributes, is_recording)
#     +-- operation
#     +-- parent_span_id
#     +-- record_exception(error)
#     +-- recorded_exceptions
#     +-- set_attribute(key, value)
#     +-- set_status(code, description)
#     +-- span_id
#     +-- status_code
#     +-- status_description
#     `-- trace_id
"""Programmatic OpenTelemetry span handle for Nyssa logging."""

from __future__ import annotations

from collections.abc import Mapping
from typing import final


@final
class SpanScope:
    """Active or no-op span context used to correlate logs and record telemetry attributes."""

    def __init__(
        self,
        name: str,
        trace_id: str = "",
        span_id: str = "",
        parent_span_id: str | None = None,
        operation: str = "",
        attributes: Mapping[str, object] | None = None,
        is_recording: bool = False,
    ) -> None:
        """Initialize span metadata and mutable attribute/status state while active."""
        self._name = name
        self._trace_id = trace_id
        self._span_id = span_id
        self._parent_span_id = parent_span_id
        self._operation = operation if operation else name
        self._attributes: dict[str, object] = dict(attributes) if attributes is not None else {}
        self._is_recording = is_recording
        self._status_code = "UNSET"
        self._status_description = ""
        self._recorded_exceptions: list[str] = []

    @property
    def name(self) -> str:
        """Return the span name."""
        return self._name

    @property
    def operation(self) -> str:
        """Return the logical operation name associated with the span."""
        return self._operation

    @property
    def trace_id(self) -> str:
        """Return the 32-character hexadecimal W3C trace ID when recording, or empty string."""
        return self._trace_id

    @property
    def span_id(self) -> str:
        """Return the 16-character hexadecimal W3C span ID when recording, or empty string."""
        return self._span_id

    @property
    def parent_span_id(self) -> str | None:
        """Return the parent 16-character hexadecimal span ID if nested, else None."""
        return self._parent_span_id

    @property
    def is_recording(self) -> bool:
        """Return True when OpenTelemetry instrumentation is active for this span."""
        return self._is_recording

    @property
    def attributes(self) -> Mapping[str, object]:
        """Return a snapshot of attributes recorded on this span."""
        return dict(self._attributes)

    @property
    def status_code(self) -> str:
        """Return the current span status code ('UNSET', 'OK', or 'ERROR')."""
        return self._status_code

    @property
    def status_description(self) -> str:
        """Return the human-readable status description."""
        return self._status_description

    @property
    def recorded_exceptions(self) -> tuple[str, ...]:
        """Return formatted exception descriptions recorded on this span."""
        return tuple(self._recorded_exceptions)

    def set_attribute(self, key: str, value: object) -> None:
        """Attach or update a structured attribute on this span when recording."""
        if not self._is_recording or not key:
            return
        self._attributes[key] = value

    def record_exception(self, error: BaseException) -> None:
        """Record an exception event on this span when recording."""
        if not self._is_recording:
            return
        self._recorded_exceptions.append(f"{type(error).__name__}: {error}")

    def set_status(self, code: str, description: str = "") -> None:
        """Set the span status code ('OK' or 'ERROR') and optional description."""
        if not self._is_recording:
            return
        self._status_code = code.upper()
        self._status_description = description
