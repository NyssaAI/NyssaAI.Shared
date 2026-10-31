# nyssa-ai-logging

`nyssa-ai-logging` provides lightweight, operational logging destinations for Python systems, matching the operational behavior and JSON schema of `NyssaAI.Logging` (`nyssa-ai-logging-cs`) and `@nyssa-ai/logging` (`nyssa-ai-logging-ts`). It delivers essential operational sinks—structured rotating JSON files, owner-only access controls, independent STDERR/STDOUT routing, and fail-safe execution—without introducing heavy external logging dependencies.

* **Rotating JSONL persistence**: Automatically rotates at 10 MiB with 5 numbered backups and 30-day retention.
* **Clean protocol streams**: Routes diagnostics to `sys.stderr` by default while keeping `sys.stdout` unpolluted for machine protocols (e.g. CLI pipes, MCP servers).
* **Cross-account privacy**: Enforces POSIX 0600 file modes / 0700 directories and Windows DACLs to prevent unauthorized cross-account log reads.
* **Crash-proof execution**: Degrades gracefully to STDERR diagnostics without throwing exceptions or aborting execution on sink errors.
* **Zero-ceremony integration**: Provides a fluent configuration builder and an action-taking `Logger` protocol for Dependency Injection.

## Simple setup, ordinary logging

Register logging during startup, then inject `Logger` into the classes that need it. `create_nyssa_logging()` registers standard error output by default, with `Information` and higher levels enabled:

```python
from nyssa_ai_logging import create_nyssa_logging

logging = (
    create_nyssa_logging("logs/service.log")
    .with_rotating_json_log("logs/application.json")
    .with_console()
)

logger = logging.create_logger("OrderService")
logger.info("Order {OrderId} placed", {"OrderId": 1042}, operation="OrderPlaced")
```

## The JSON file

The log file contains one complete JSON object per line matching the cross-language record schema:

```json
{
  "Timestamp": "2026-10-12T14:23:45.123456+00:00",
  "Level": "Information",
  "Message": "Order 1042 placed",
  "Module": "OrderService",
  "Operation": "OrderPlaced",
  "Exception": null,
  "AdditionalFields": {
    "OrderId": 1042
  }
}
```

## Rotation and retention

| Setting | Behavior |
| --- | --- |
| Rotation threshold | 10 MiB, or 10,485,760 bytes. |
| Backup count | Up to five numbered backups beside the active file. |
| Backup names | `service.log.1` is the newest; `service.log.5` is the oldest. |
| Age limit | Backups older than 30 days are pruned at startup and during rotation. |
