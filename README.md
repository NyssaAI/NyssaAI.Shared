# NyssaAI.Shared

Reusable .NET libraries maintained as independently versioned packages. Each library is prepared and added separately.

| Library | Consumer API | Purpose |
| --- | --- | --- |
| [NyssaAI.Result](NyssaAI.Result/README.md) | `NyssaAI.Results.Result`, `Result<T>`, `Error`, `Guard` | Represent outcomes, compose fallible operations, and validate input. |
| [NyssaAI.Configuration](NyssaAI.Configuration/README.md) | `NyssaAI.Configuration.IConfiguration` | Persist typed settings and resolve encrypted, OAuth, or provider-backed values. |
| [NyssaAI.Logging](NyssaAI.Logging/README.md) | Microsoft `ILogger<T>` | Write to standard error and rotating structured JSON files. |
| [NyssaAI.Repository](NyssaAI.Repository/README.md) | `NyssaAI.Repository.IRepository` | Run typed database commands in sessions and transactions, with explicit migrations. |

## Build and test

Install the .NET SDK version pinned in `global.json`. From the repository root:

```powershell
dotnet restore NyssaAI.Shared.slnx --locked-mode
dotnet build NyssaAI.Shared.slnx --configuration Release
./run-tests.ps1
```

`run-tests.ps1` builds current source before running all eight unit and public API tutorial suites. A build or test failure returns a failing exit code. Configuration and Logging tests allow platform-specific skips; real keyring tests need explicit opt-in, and live-service tests are excluded. The other suites fail on skips.

Repository tests use SQLite and PostgreSQL. The runner starts and removes a disposable PostgreSQL container when Docker supports Linux containers. Otherwise, it can create a temporary database cluster using PostgreSQL binaries on `PATH` or in `PGBIN`; on Windows it also checks the standard installation directory. To supply your own isolated test database, set `NYSSAAI_REPOSITORY_POSTGRES`. Tests create and drop tables, so do not use an application database.

GitHub Actions runs the full suite and package-consumer check on Windows and Linux. A separate Linux job publishes and executes the native smoke applications. Run these additional checks locally with:

```powershell
./test/verify-packages.ps1
```

```sh
# On Linux, with Native AOT prerequisites and NYSSAAI_REPOSITORY_POSTGRES set:
bash test/verify-native.sh
```

The package check inspects all four NuGet archives, then restores and runs a separate consumer with a fresh package cache. The native checks cover Logging and Repository, both database providers, and encrypted settings across process runs.

For one executable tutorial suite:

```powershell
dotnet run --project test/NyssaAI.Result.UseCaseTests
```

## Package and repository boundaries

Each package owns its source, unit tests, and documentation. Root `test/` contains public API tutorials and tests that span packages. Shared build settings and dependency versions live at the root. Package names and versions remain independent of the repository name.

`Directory.Packages.props` specifies direct dependency versions. Committed `packages.lock.json` files pin the resolved dependency graph and package hashes; CI checks them with a locked restore. After an intentional dependency update, run `dotnet restore NyssaAI.Shared.slnx --force-evaluate` and review the lock-file changes alongside the version changes. Native publishes also resolve runtime-specific dependencies for their target platform.

Library directories, projects, assemblies, and package IDs use `NyssaAI.*`. Result's public namespace is `NyssaAI.Results`; the other libraries use their package names. Consumers of the earlier names must update package references, `using` directives, and fully qualified type names. Repository exposes `IRepository`, implemented by `RepositoryClient`. Logging uses Microsoft's existing logging interfaces.

Release versions are pinned in each library project: Result `0.2.1`, Configuration `0.2.1`, Logging `0.2.1`, and Repository `0.2.1`. Package-specific Git tags use `NyssaAI.<Package>/v<version>` and identify the release commit.

## Licensing

These libraries are licensed under the [Apache License, Version 2.0](LICENSE.txt). Each NuGet package includes the license; the build workflow does not publish packages.
