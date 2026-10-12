# Application validation and framework failure boundaries

This package contains values and per-operation construction APIs, so it needs no Microsoft DI registration. Its tutorials construct those values directly; service libraries use scoped DI registrations.

Run these public API tutorials from the repository root with the .NET 10 SDK:

```powershell
dotnet run --project test/Nyssa.Result.UseCaseTests
```

Read [the executable examples](../../../test/Nyssa.Result.UseCaseTests/ResultTutorials.cs) from Arrange through Cleanup. The Act sections are copyable consumer workflows using public contracts; xUnit assertions and temporary-resource setup stay outside them.

- `Then_OrderValidation_CalculatesAReceiptAndShortCircuitsInvalidInput` composes guard validation, transforms the successful value, and resolves both a receipt and correction feedback.

- `ThrowIfError_ExplicitValidationBoundary_StopsInvalidWorkAndAllowsRecovery` uses `Guard.Against` with opt-in `.ThrowIfError()`, preserves validation errors through `Result.Try`, and retries with valid input.

- `Try_MissingImport_ReturnsNotFoundWithoutExposingThePathToTheUser` translates a framework file exception into a categorized error with separate user and technical messages.

- `TryAsync_CanceledImport_ReportsCanceledAndSkipsSuccessTransformation` passes the caller token through an asynchronous file operation and maps deliberate cancellation to a no-retry response.

The package is named `Nyssa.Result`; consumer types live in `NyssaAI.Results`. `Then` chains fallible operations, `Transform` computes from success, and `Resolve` turns either branch into application output. `Try` and `TryAsync` belong at throwing framework or vendor boundaries. File scenarios use unique local temporary directories and clean them in `finally`; no external service is required.

The project is a separate consumer assembly and receives no friend-assembly access. It references the production project, so public API changes that invalidate these examples fail compilation. Assertions check consumer-visible values and artifacts. No library behavior is changed by these tutorials.

See the [repository README](../../../README.md) for the complete build and test commands.
