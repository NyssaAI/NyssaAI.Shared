# Logging and Repository import decisions

The two libraries follow the same package structure as Configuration and Result: source and unit tests inside each library, public API tutorials under root `test/`, and independently versioned NuGet packages. This records the choices that needed more than a rename.

## Use the existing implementations

Both libraries already had working implementations and tests in the earlier shared-library repository. Importing that behavior gave this repository a tested starting point. Obsolete examples, excluded tests, generated output, old planning records, and historical performance reports were left out. The new checkout builds without access to the original repository.

Logging keeps version `2.0.0`; Repository keeps `7.0.0`. The package IDs and namespaces change to `NyssaAI.*`, so consumers must update their references. The previously requested patch increments are included: Configuration `7.0.1` and Result `0.6.1`. No package is published by this change.

## Keep Microsoft's logging interface

Logging is a provider for the logging system applications already use. Adding an `ILogging` client would duplicate `ILogger<T>` and make normal framework integration harder. The public additions are the registration extensions in `NyssaAI.Logging.Configuration`. File writing, formatting, rotation, and permissions stay internal. Logging does not depend on Configuration or Result.

Two defects surfaced during import. A provider-wide minimum-level rule let one file change another file's filtering, and Windows permission updates could leave an existing explicit grant in place. Five regression cases reproduced those failures before the fixes. Each file now applies its own minimum after host filtering; owned Windows files receive a replacement protected access rule set for the current user. Existing parent directories retain their permissions.

File writes are synchronized within one provider instance. Separate processes or instances must use separate paths. This import does not add a shared file coordinator or a durable logging queue.

## Give Repository one clear application interface

`IRepository` replaces the earlier `IRepositoryBase` name. `RepositoryClient` implements it and coordinates command selection and scope creation. Applications own their commands and SQL; providers open database connections. Sessions and transactions own those connections and must be disposed by the caller.

SQLite and PostgreSQL remain built-in providers. The existing typed registration mechanism supports application providers without reflection or assembly scanning. Dapper and Dapper.AOT belong to the example command assembly, not the Repository package. Custom commands can use another database API while following the same scope rules.

Direct registration needs no configuration host. Repository retains a package dependency on Configuration because its optional persisted-settings extensions are part of this package. Splitting that integration into another package would change the established distribution boundary without improving this import.

## Preserve stored names

The configuration namespace and key remain `nyssa.repository/default`. Migration history remains in `nyssa_repository_migrations`. Renaming these with the assembly would silently disconnect applications from existing settings and schema history. No data migration is implied by the new package name.

Repository reads a settings snapshot at registration. Updating saved settings affects a future registration, not a provider already in use. Migrations remain explicit and are never run by service registration.

## Test real database behavior

PostgreSQL tests fail when no test database is supplied; they are not optional green checks. The runner normally creates a uniquely named disposable Docker container with a generated password and a loopback port. A native PostgreSQL fallback supports Windows environments with installed binaries. An explicitly supplied connection string must name an isolated test database.

SQLite tests use isolated files or in-memory databases. Repository tests cover command resolution, custom providers, connection ownership, cancellation, transaction failure and rollback, pipelines, migration history, and protected configuration. Public tutorials compile in separate assemblies without access to library internals.

## Check packages and native consumers separately

The package check packs all four libraries, checks their identities, assembly, README, and license, then compiles and runs a separate application against those archives. It uses a fresh package cache and maps `NyssaAI.*` to the local feed. The application executes a typed SQLite command and verifies its structured log entry.

Native AOT checks compile on Linux for Linux. The native applications exercise file logging, generated database commands, both providers, transactions, pipelines, migrations, encrypted settings across process runs, and rejection of an incorrect key. These checks establish the exercised Linux x64 paths; they do not establish every driver feature, custom command, or platform. macOS and Windows native execution were not tested during this import. Microsoft's [Native AOT requirements](https://learn.microsoft.com/dotnet/core/deploying/native-aot/) describe the platform toolchains.

SQLite's async API executes database work synchronously. Cancellation before work starts is covered, but prompt interruption of an already-running SQLite statement is not promised. PostgreSQL in-flight cancellation is exercised.

## Dependencies and licensing

The package manifests retain the repository's PolyForm Small Business license and include its text. Direct third-party packages remain NuGet dependencies, with their own notices and terms. The inspected package metadata identifies Microsoft logging and SQLite packages as MIT, Npgsql as PostgreSQL, and Dapper, Dapper.AOT, and SQLitePCLRaw as Apache-2.0. No third-party package source or binary is copied into these library packages. The unused Testcontainers reference was removed.

## Verification

On Windows and Linux, the Release solution build completed with warnings treated as errors, and all eight suites completed without failures: 900 cases per platform, with 91 documented platform or opt-in skips. All 286 Repository cases ran on both platforms. The Logging suite ran the applicable permissions checks on each platform. The package consumer passed on both platforms.

Both Linux x64 native applications compiled and ran successfully. Repository checks passed against SQLite and PostgreSQL, including the cross-process encrypted-settings and wrong-key cases. macOS and Windows native execution remain outside the observed evidence.

Full code review completed with no actionable findings across correctness, tests, public contracts, security, reliability, performance, and migrations. The adversarial review used a local fallback because the external launcher lacked jq.
