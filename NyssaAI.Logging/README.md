# NyssaAI.Logging

`NyssaAI.Logging` gives .NET applications a simple way to write logs to standard error and to a rotating JSON file. Application classes use the standard `ILogger<T>` interface. The library configures the destinations and handles the file's format, permissions, and rotation.

The package targets .NET 10 and enables Native AOT compatibility checks. It requires no configuration store or separate logging interface.

## Simple setup, ordinary logging

Register logging once during startup, then inject `ILogger<T>` into the classes that need it. The default setup writes `Information` and higher levels to standard error:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NyssaAI.Logging.Configuration;

services.AddNyssaLogging();
```

To write to a file as well, supply its path instead:

```csharp
services.AddNyssaLogging("logs/service.log");
```

Application code uses the same logging calls for both destinations. Here, `logger` is an injected `ILogger<OrderService>`:

```csharp
logger.LogInformation("Order {OrderId} placed", orderId);
```

The library supports a few common ways to use logging:

- **Write to standard error.** Call `AddNyssaLogging()` without a file path. This keeps standard output available for command output or piped data.
- **Keep a local log.** Supply a file path to add structured JSON records alongside standard error.
- **Use existing logging setup.** Add `AddNyssaJsonFile` to your application's `AddLogging` call.
- **Keep searchable values.** Use message placeholders such as `{OrderId}`. Their values are stored separately from the formatted message.
- **Choose a minimum level.** Set the application's minimum, or give an individual file a higher minimum.

## Register the destinations

[`AddNyssaLogging`](src/Configuration/LoggingServiceCollectionExtensions.cs) configures Microsoft's console provider to send all its output to standard error. When a file path is supplied, it also registers the JSON file provider. It keeps any other logging providers already registered by the application.

```csharp
services.AddNyssaLogging("logs/service.log", LogLevel.Warning);
```

This changes the default minimum for the logging factory to `Warning`. Microsoft's category and provider filters still apply.

If the application already configures logging, add just the file destination:

```csharp
services.AddLogging(logging => logging.AddNyssaJsonFile("logs/service.log"));
```

`AddNyssaJsonFile` adds no console output. You can combine it with other Microsoft logging providers, including a console provider, a telemetry exporter, or a remote log service.

### Choose which records reach a file

The host's filters decide which records reach the provider. A file can apply a higher minimum to the records it receives:

```csharp
services.AddLogging(logging => logging
    .SetMinimumLevel(LogLevel.Information)
    .AddNyssaJsonFile("logs/service.log")
    .AddNyssaJsonFile("logs/errors.log", LogLevel.Error));
```

The first file receives `Information` and higher levels. The second receives only `Error` and `Critical`. Each file keeps its own minimum; a lower file minimum does not override the host's filtering.

### Choose the path

There is no default file path. The path must include a directory: `logs/service.log` is valid, while `service.log` alone disables the file destination and reports the reason on standard error.

Relative paths use the process's working directory. To place logs beneath the application's directory, supply that path explicitly:

```csharp
services.AddNyssaLogging(Path.Combine(AppContext.BaseDirectory, "logs", "service.log"));
```

Choose a location the application's account can write to. The library creates a missing directory and appends to an existing file. It does not move a log when the working directory or application location changes.

`NyssaAI.Configuration` is optional. An application can load a path from its configuration service during startup and pass it to `AddNyssaLogging`; the logging package does not open or retain a configuration store.

## What happens during startup

Registration adds the logging services to the container. The file is opened when the logging provider is created, usually when the host or logger factory first resolves logging.

1. **Prepare the destination.** Create a missing directory, open the file for appending, and apply file permissions.
2. **Remove expired backups.** Delete the numbered backups whose last-write time is more than 30 days old.
3. **Create the logger.** Microsoft's logger factory supplies the category and applies its filters.
4. **Write each record.** Format the message, capture its structured fields, and append one JSON object followed by a newline.

File writes happen synchronously under a lock belonging to that provider. Each record is flushed from the stream before the call returns; there is no background queue. This does not guarantee survival of a machine or storage failure.

## The JSON file

The file contains one complete JSON object per line. This example is expanded for readability:

```json
{
  "Timestamp": "2026-10-12T14:23:45.1234567Z",
  "Level": "Information",
  "Message": "Order 1042 placed",
  "Module": "MyApp.OrderService",
  "Operation": "OrderPlaced",
  "Exception": null,
  "AdditionalFields": {
    "OrderId": 1042
  }
}
```

The fields have fixed names:

| Field | What it contains |
| --- | --- |
| `Timestamp` | UTC time in ISO-8601 round-trip format. |
| `Level` | The logging level, such as `Information` or `Error`. |
| `Message` | The formatted message, including its supplied values. |
| `Module` | The logger category. For `ILogger<T>`, this is the type's full name. |
| `Operation` | The `EventId` name, or an empty string when none is supplied. The numeric event ID is not stored. |
| `Exception` | The exception's full `ToString()` output, or `null`. |
| `AdditionalFields` | Values supplied through message-template placeholders. |

Name an operation by passing an `EventId`:

```csharp
logger.LogInformation(new EventId(10, "OrderPlaced"), "Order {OrderId} placed", orderId);
```

Pass an exception to include its details:

```csharp
logger.LogError(exception, "Could not save order {OrderId}", orderId);
```

### Structured values

Use placeholders to name the values you want to search later:

```csharp
logger.LogInformation("Order {OrderId} total is {Amount}", orderId, amount);
```

Strings, booleans, `int`, `long`, finite `float` and `double` values, and `decimal` values retain their JSON types. Null values become JSON `null`. Dates and GUIDs become strings. Other values, including nested objects and collections, use their `ToString()` output; the provider does not serialize their properties.

The original message template is not repeated in `AdditionalFields`. Logging scopes are not captured by this file provider. It also performs no automatic redaction, so choose message values and exception details with that in mind.

The provider uses a registered `TimeProvider` when one is available, or `TimeProvider.System` otherwise. This allows tests to control record timestamps and backup age checks.

## Rotation and retention

The library handles rotation using fixed settings:

| Setting | Behavior |
| --- | --- |
| Rotation threshold | 10 MiB, or 10,485,760 bytes. |
| Backup count | Up to five numbered backups beside the active file. |
| Backup names | `service.log.1` is the newest; `service.log.5` is the oldest. |
| Age limit | Backups older than 30 days are removed at startup and during rotation. |

Before writing a record, the provider checks whether the active file already exceeds the threshold. If it does, it shifts the backups, archives the active file, and opens a new one. A large record can push a file over the threshold, so this is not a strict disk-space limit.

If rotation fails, the provider reports it on standard error and tries to reopen the active path. When reopening succeeds, it waits for another 10 MiB of growth before retrying rotation. If reopening fails, that provider stops writing to the file.

Use a separate path for each process or independently registered provider. The file permits shared access, but the write lock is local to one provider. Separate writers can interfere with both appends and rotation.

## Permissions and failure behavior

The library attempts to restrict the log file and directories it creates to the current account:

- **Linux and macOS.** New directories use `0700`; files use `0600`. The file's mode is also tightened when an existing log is opened.
- **Windows.** Files use an ACL granting the current user full control, with inherited and previous access rules removed. Newly created log directories receive the same restriction.
- **Existing directories.** Their permissions are left unchanged. A shared directory supplied by the operator remains shared.

Permission changes are best effort. A failure is reported on standard error and does not itself disable logging. This is not an encrypted or guaranteed private store.

An unavailable file destination should not prevent the application from starting. The library handles ordinary file failures as follows:

| Failure | What happens |
| --- | --- |
| The file cannot be opened | Report the reason on standard error and leave the file destination inactive. Other providers continue to work. |
| A record cannot be formatted or written | Report the error and drop that record. A later call can try again while the stream remains open. |
| Rotation fails | Report the error and attempt to reopen the active path. |
| A backup cannot be deleted | Report a warning and continue. |
| Standard error cannot accept the library's diagnostic | Drop the diagnostic. |

Invalid registration arguments, such as a null builder or an empty file path, throw during registration. `OutOfMemoryException` is not swallowed. The logging API follows Microsoft's `ILogger` contract and does not return `Result` objects.

The host or logger factory owns the provider and disposes its stream. Dispose the host when the application shuts down. Repeated disposal is safe; calls reaching an already disposed file provider are ignored.

## How the library is organized

`ILogger<T>` is the interface used by application classes. Microsoft's logger factory sends each accepted record to the registered providers. NyssaAI's internal file provider converts the record to JSON and delegates writing and rotation to its file implementation.

```text
Application
    |
ILogger<T>
    |
Microsoft logger factory
    +-- Console provider --> Standard error
    +-- NyssaAI file provider --> JSON log file
    +-- Other registered providers
```

[`LoggingServiceCollectionExtensions`](src/Configuration/LoggingServiceCollectionExtensions.cs) provides the simple setup call. [`LoggingBuilderExtensions`](src/Configuration/LoggingBuilderExtensions.cs) adds an individual file destination. Both are in `NyssaAI.Logging.Configuration`.

The implementation uses Microsoft's `ILoggerProvider` contract. It does not introduce an `ILogging` wrapper or a separate client. This provider exception is recorded in the project file and checked by the package's surface tests.

Records are written with `Utf8JsonWriter`, using explicit handling for each supported field type. The library does not rely on reflection-based JSON serialization. The package enables trimming and Native AOT analyzers; applications must also verify their other logging providers and dependencies when publishing for Native AOT.

## Run the tutorials and tests

The [executable tutorials](docs/tutorials/README.md) show registration, ordinary logging, structured fields, filtering, and continuing when a file cannot be opened. They use temporary directories and exercise the public registration calls.

The [unit tests](test/NyssaAI.Logging.Tests) cover the JSON format, permissions, rotation, retention, filtering, failure reporting, and disposal. Platform-specific permission tests run on their matching operating system.

Run the repository's test suites from its root:

```powershell
./run-tests.ps1
```

## Package and licensing

The package and assembly are both named `NyssaAI.Logging`. The registration extensions are in `NyssaAI.Logging.Configuration`. Its dependencies are Microsoft's logging, console logging, and dependency-injection abstractions packages.

Refer to the repository's [licensing notice](../README.md#licensing) and [LICENSE.txt](../LICENSE.txt) for terms of use.
