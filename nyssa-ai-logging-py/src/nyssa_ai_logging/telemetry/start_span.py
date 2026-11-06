# Responsibility: Manage OpenTelemetry span lifecycle, context propagation, and correlation state.
# Public interface:
# +-- get_active_span()
# +-- is_otel_enabled()
# +-- set_otel_enabled(enabled)
# `-- start_span(name, attributes, operation)
"""Context manager and runtime state for programmatic OpenTelemetry spans."""

from __future__ import annotations

from collections.abc import Iterator, Mapping
from contextlib import contextmanager
from contextvars import ContextVar
import secrets

from opentelemetry import trace as otel_trace
from opentelemetry.trace import NonRecordingSpan, SpanContext, TraceFlags

from nyssa_ai_logging.telemetry.span_scope import SpanScope

_ACTIVE_SPAN: ContextVar[SpanScope | None] = ContextVar("_nyssa_active_span", default=None)
_OTEL_ENABLED: bool = False


def set_otel_enabled(enabled: bool) -> None:
    """Enable or disable OpenTelemetry span creation and log correlation."""
    global _OTEL_ENABLED
    _OTEL_ENABLED = enabled


def is_otel_enabled() -> bool:
    """Return True when OpenTelemetry integration is enabled."""
    return _OTEL_ENABLED


def get_active_span() -> SpanScope | None:
    """Return the active SpanScope if OpenTelemetry is enabled and a span is active."""
    if not _OTEL_ENABLED:
        return None
    active = _ACTIVE_SPAN.get()
    if active is not None and active.is_recording:
        return active

    otel_span = otel_trace.get_current_span()
    ctx = otel_span.get_span_context()
    if ctx.is_valid:
        return SpanScope(
            name="",
            trace_id=otel_trace.format_trace_id(ctx.trace_id),
            span_id=otel_trace.format_span_id(ctx.span_id),
            parent_span_id=None,
            operation="",
            is_recording=True,
        )
    return None


@contextmanager
def start_span(
    name: str,
    attributes: Mapping[str, object] | None = None,
    operation: str = "",
) -> Iterator[SpanScope]:
    """Start a correlated OpenTelemetry span around a block of work when OTel is enabled."""
    if not _OTEL_ENABLED:
        yield SpanScope(name=name, operation=operation, is_recording=False)
        return

    parent = _ACTIVE_SPAN.get()
    parent_span_id: str | None = None
    trace_int: int = 0

    if parent is not None and parent.is_recording and parent.trace_id:
        trace_int = int(parent.trace_id, 16)
        parent_span_id = parent.span_id
    else:
        current_otel = otel_trace.get_current_span().get_span_context()
        if current_otel.is_valid:
            trace_int = current_otel.trace_id
            parent_span_id = otel_trace.format_span_id(current_otel.span_id)

    if trace_int == 0:
        trace_int = secrets.randbits(128) or 1
    span_int = secrets.randbits(64) or 1

    trace_hex = otel_trace.format_trace_id(trace_int)
    span_hex = otel_trace.format_span_id(span_int)

    scope = SpanScope(
        name=name,
        trace_id=trace_hex,
        span_id=span_hex,
        parent_span_id=parent_span_id,
        operation=operation if operation else name,
        attributes=attributes,
        is_recording=True,
    )

    span_context = SpanContext(
        trace_id=trace_int,
        span_id=span_int,
        is_remote=False,
        trace_flags=TraceFlags(TraceFlags.SAMPLED),
    )
    otel_span_wrapper = NonRecordingSpan(span_context)
    token = _ACTIVE_SPAN.set(scope)

    try:
        with otel_trace.use_span(otel_span_wrapper, end_on_exit=False):
            yield scope
            if scope.status_code == "UNSET":
                scope.set_status("OK")
    except BaseException as exc:
        scope.record_exception(exc)
        scope.set_status("ERROR", str(exc))
        raise
    finally:
        _ACTIVE_SPAN.reset(token)
