# Repository native smoke application

This executable exercises the actual generated command assembly against SQLite and PostgreSQL. It checks typed resolution, buffered results, sessions, transaction rollback and commit, dependent pipelines, task workflows, and schema migrations. It is also runnable as a managed application while diagnosing failures.

Publish from the monorepo root on a machine with the toolchain for the selected runtime:

```sh
dotnet publish test/NyssaAI.Repository.AotSmoke/NyssaAI.Repository.AotSmoke.csproj -c Release -r linux-x64 -o .temp/repository-native
```

Use an isolated database. The smoke application creates and drops its own test tables and migration history.

```sh
NYSSAAI_REPOSITORY_CONNECTION='Data Source=.temp/repository-native.db;Pooling=False' .temp/repository-native/NyssaAI.Repository.AotSmoke verify sqlite
NYSSAAI_REPOSITORY_POSTGRES="$TEST_DATABASE_CONNECTION" .temp/repository-native/NyssaAI.Repository.AotSmoke verify postgresql
```

The process exits nonzero on failure. PostgreSQL verifies cancellation of in-flight work. SQLite's synchronous execution path does not guarantee prompt interruption of a running statement.

The additional `setup <directory> <provider>`, `run <directory>`, and `switch <directory>` commands exercise persisted encrypted settings across process runs. They require `NYSSAAI_REPOSITORY_KEY` containing a base64-encoded 32-byte test key. Setup reads the connection from `NYSSAAI_REPOSITORY_POSTGRES` for PostgreSQL or `NYSSAAI_REPOSITORY_CONNECTION` for SQLite. Switch uses `NYSSAAI_REPOSITORY_POSTGRES` for its PostgreSQL target. These commands use a test keyring, so they do not depend on the machine's credential store.

Published results establish support for the exercised commands and target runtime. They do not establish AOT compatibility for every driver feature, custom command, operating system, or architecture.
