# nyssa-ai-logging

`nyssa-ai-logging` configures Python's standard `logging` module with operational destinations matching `NyssaAI.Logging` (`nyssa-ai-logging-cs`) and `@nyssa-ai/logging` (`nyssa-ai-logging-ts`). Application and library code uses idiomatic `logging.getLogger(__name__)` without constructor-injecting custom logger protocols, while `configure_nyssa_logging()` wires up safe default `logging.Handler`s (`sys.stderr` and a dated rotating JSONL file in `./logs`) and optional OpenTelemetry log/span correlation.

* **Zero-ceremony stdlib `logging`**: Call `configure_nyssa_logging()` once at startup and use `logging.getLogger(__name__)` everywhere.
* **Safe defaults out of the box**: Automatically configures `stderr` and `./logs/yyyy.mm.dd-{appname}-log.json` (both overridable).
* **Rotating JSONL persistence**: Rotates at 10 MiB with 5 numbered backups and 30-day retention.
* **Clean protocol streams**: Routes terminal diagnostics to `sys.stderr` by default while keeping `sys.stdout` unpolluted for machine protocols (CLI pipes, MCP servers).
* **Cross-account privacy**: Enforces POSIX `0600` file modes / `0700` directories and Windows DACLs to prevent unauthorized cross-account log reads.
* **OpenTelemetry integration**: Pass `enable_otel=True` to bridge stdlib logs to OpenTelemetry and correlate records with programmatic spans via `start_span(...)`.
* **Per-package & custom handler control**: Adjust levels per package hierarchy (`a.b.c`) or attach `create_json_file_handler`, `create_stderr_handler`, and `create_console_handler` directly.

## Simple setup, ordinary Python logging

Configure logging once during startup, then use standard `logging.getLogger(__name__)` in any module or class:

```python
import logging
from nyssa_ai_logging import configure_nyssa_logging, start_span

# Zero-arg safe defaults: stderr + ./logs/yyyy.mm.dd-{appname}-log.json
configure_nyssa_logging()

logger = logging.getLogger(__name__)
logger.info("Order {OrderId} placed", 1042, extra={"operation": "OrderPlaced"})
```

Override defaults, configure per-package levels (`a.b.c`), or enable OpenTelemetry in a single call:

```python
telemetry = configure_nyssa_logging(
    app_name="order-service",
    log_dir="./logs",
    minimum_level="Information",
    enable_otel=True,
    package_levels={
        "nyssa_ai_dbgateway": "Debug",
        "urllib3": "Error",
    },
)

with start_span("OrderPlaced", {"Tenant": "acme"}):
    logger.info("Order {OrderId} placed", 1042)
```

## The JSON file

The log file contains one complete JSON object per line matching the cross-language record schema:

```json
{
  "Timestamp": "2026-10-12T14:23:45.123456+00:00",
  "Level": "Information",
  "Message": "Order 1042 placed",
  "Module": "orders.service",
  "Operation": "OrderPlaced",
  "Exception": null,
  "AdditionalFields": {
    "Tenant": "acme",
    "TraceId": "4bf92f3577b34da6a3ce929d0e0e4736",
    "SpanId": "00f067aa0ba902b7",
    "OrderId": 1042
  }
}
```

## Rotation and retention

| Setting | Behavior |
| --- | --- |
| Default path | `./logs/yyyy.mm.dd-{appname}-log.json` (overridable via `app_name`, `log_dir`, or `file_path`). |
| Rotation threshold | 10 MiB, or 10,485,760 bytes. |
| Backup count | Up to five numbered backups beside the active file. |
| Backup names | `service.log.1` is the newest; `service.log.5` is the oldest. |
| Age limit | Backups older than 30 days are pruned at startup and during rotation. |
