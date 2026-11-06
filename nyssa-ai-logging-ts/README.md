# @nyssa-ai/logging

`@nyssa-ai/logging` provides lightweight, zero-injection operational logging for TypeScript and Node.js systems, wrapping **Pino** and **OpenTelemetry** while matching the operational behavior and JSON schema of `NyssaAI.Logging` (`nyssa-ai-logging-cs`) and `nyssa-ai-logging` (`nyssa-ai-logging-py`).

* **No injected loggers required**: Any module or library package simply calls `getLogger("a.b.c")` at module scope.
* **One-call safe defaults**: `configureNyssaLogging()` automatically configures `process.stderr` + a rotating JSONL file at `./logs/{yyyy.mm.dd}-{appname}-log.json`.
* **Per-package level filtering (`a.b.c`)**: Adjust verbosity per package hierarchy via `packageLevels` or `.setPackageLevel("nyssa_ai_dbgateway", "Debug")`.
* **OpenTelemetry integration**: Turn on OpenTelemetry log bridging with `enableOtel: true` and correlate spans across async workflows with `startSpan(...)`.
* **Rotating JSONL persistence & privacy**: Automatically rotates at 10 MiB with 5 numbered backups, 30-day retention, and owner-only permissions (`0600` files / `0700` directories on POSIX, Windows `icacls` DACLs).

## 1. Zero-ceremony setup & module-scoped logging

Call `configureNyssaLogging()` once at startup:

```typescript
import { configureNyssaLogging, getLogger } from "@nyssa-ai/logging";

// Safe defaults: stderr + ./logs/{yyyy.mm.dd}-{appname}-log.json
configureNyssaLogging();

// In any module or package — no constructor logger injection needed:
const logger = getLogger("billing.orders");
logger.info("Order {OrderId} placed", { OrderId: 1042 }, "OrderPlaced");
```

## 2. Customizing directories, per-package levels, and OpenTelemetry

```typescript
import { configureNyssaLogging, getLogger, startSpan } from "@nyssa-ai/logging";

const telemetry = configureNyssaLogging({
  appName: "billing-worker",
  logDir: "/var/log/nyssa", // Creates /var/log/nyssa/2026.11.05-billing-worker-log.json
  minimumLevel: "Information",
  enableOtel: true,
  packageLevels: {
    "nyssa_ai_dbgateway": "Debug", // Verbose SQL diagnostics for this package tree
    "noisy.thirdparty": "Error"    // Silence noisy third-party modules
  }
})
  .setPackageLevel("billing.audit", "Debug")
  .withConsole();

const logger = getLogger("billing.checkout");

await startSpan("checkout-order", { OrderId: 1042, CustomerTier: "Enterprise" }, "OrderCheckout", async (span) => {
  logger.info("Validating cart items");
  span.setAttribute("ItemCount", 3);

  await startSpan("authorize-payment", { Gateway: "Stripe" }, "PaymentAuth", async () => {
    logger.info("Card authorized for {Amount}", { Amount: 149.99 });
  });
});
```

## 3. Canonical JSON file format

```json
{
  "Timestamp": "2026-11-05T14:23:45.123Z",
  "Level": "Information",
  "Message": "Card authorized for 149.99",
  "Module": "billing.checkout",
  "Operation": "PaymentAuth",
  "Exception": null,
  "AdditionalFields": {
    "OrderId": 1042,
    "CustomerTier": "Enterprise",
    "Gateway": "Stripe",
    "Amount": 149.99,
    "TraceId": "4bf92f3577b34da6a3ce929d0e0e4736",
    "SpanId": "00f067aa0ba902b7",
    "ParentSpanId": "b9c7c989f97918e1"
  }
}
```
