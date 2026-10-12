# NyssaAI.Shared

Reusable .NET libraries maintained as independently versioned packages. Each library is prepared and added separately.

| Library | Consumer API | Purpose |
| --- | --- | --- |
| [Nyssa.Result](Nyssa.Result/README.md) | `NyssaAI.Results.Result`, `Result<T>`, `Error`, `Guard` | Represent outcomes, compose fallible operations, and validate input. |
| [Nyssa.Configuration](Nyssa.Configuration/README.md) | `NyssaAI.Configuration.IConfiguration` | Persist typed settings and resolve encrypted, OAuth, or provider-backed values. |

## Build and test

Install a stable .NET 10 SDK. From the repository root:

```powershell
dotnet build NyssaAI.Shared.slnx --configuration Release
./run-tests.ps1
```

`run-tests.ps1` builds current source before running the unit and public API tutorial suites. A build or test failure returns a failing exit code. Result tests also fail on skips. Configuration tests skip platform-specific cases and real keyring cases unless explicitly enabled; live-service tests are excluded. GitHub Actions runs the same script on Windows and Linux.

For one executable tutorial suite:

```powershell
dotnet run --project test/Nyssa.Result.UseCaseTests
```

## Package and repository boundaries

Each package owns its source, unit tests, and documentation. Root `test/` contains public API tutorials and tests that span packages. Shared build settings and dependency versions live at the root. Package names and versions remain independent of the repository name.

Public namespaces use `NyssaAI.*`. Package and assembly names remain `Nyssa.Result` and `Nyssa.Configuration`. Code imported from the former namespaces must update its `using` directives and fully qualified type names to `NyssaAI.Results` and `NyssaAI.Configuration`.

Only prepared libraries are included. See the [library import process](docs/library-imports.md) before adding another package.

## Licensing

Commercial licensing terms are being finalized. No open-source or commercial license is offered in this repository yet; see the [licensing policy under preparation](docs/licensing-policy.md). Packages are not published by the build workflow.
