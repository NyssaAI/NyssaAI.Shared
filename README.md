# NyssaAI.Shared

Reusable .NET libraries maintained as independently versioned packages. Each library is prepared and added separately.

| Library | Consumer API | Purpose |
| --- | --- | --- |
| [NyssaAI.Result](NyssaAI.Result/README.md) | `NyssaAI.Results.Result`, `Result<T>`, `Error`, `Guard` | Represent outcomes, compose fallible operations, and validate input. |
| [NyssaAI.Configuration](NyssaAI.Configuration/README.md) | `NyssaAI.Configuration.IConfiguration` | Persist typed settings and resolve encrypted, OAuth, or provider-backed values. |

## Build and test

Install a stable .NET 10 SDK. From the repository root:

```powershell
dotnet build NyssaAI.Shared.slnx --configuration Release
./run-tests.ps1
```

`run-tests.ps1` builds current source before running the unit and public API tutorial suites. A build or test failure returns a failing exit code. Result tests also fail on skips. Configuration tests skip platform-specific cases and real keyring cases unless explicitly enabled; live-service tests are excluded. GitHub Actions runs the same script on Windows and Linux.

For one executable tutorial suite:

```powershell
dotnet run --project test/NyssaAI.Result.UseCaseTests
```

## Package and repository boundaries

Each package owns its source, unit tests, and documentation. Root `test/` contains public API tutorials and tests that span packages. Shared build settings and dependency versions live at the root. Package names and versions remain independent of the repository name.

Library directories, projects, assemblies, and package IDs use `NyssaAI.Result` and `NyssaAI.Configuration`. Public namespaces use `NyssaAI.Results` and `NyssaAI.Configuration`. Consumers of the earlier names must update their package references as well as their `using` directives and fully qualified type names.

Only prepared libraries are included. See the [library import process](docs/library-imports.md) before adding another package.

## Licensing

These libraries are source-available under the unmodified [PolyForm Small Business License 1.0.0](LICENSE.txt). Businesses outside its eligibility limits need a separate paid commercial license. See the [licensing policy](docs/licensing-policy.md) for the thresholds and commercial licensing route. Each NuGet package includes the license; the build workflow does not publish packages.
