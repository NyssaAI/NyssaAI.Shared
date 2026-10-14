using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.AotCommands.Migrations;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Migrations;
using NyssaAI.Repository.Providers;

namespace NyssaAI.Repository.AotSmoke;

internal static class MigrationChecks
{
    internal static async Task<int> RunAsync(string providerName, string connectionString)
    {
        await using (var container = CreateContainer(providerName, connectionString, MigrationFixtures.Create(providerName)))
        {
            var provider = container.GetRequiredService<IRepositoryProvider>();
            var runner = container.GetRequiredService<IMigrationRunner>();
            await ResetAsync(provider);
            foreach (var sql in new[]
            {
                "CREATE TABLE example(id); /* outer /* inner */ COMMIT; -- */",
                "CREATE TABLE example(id int); SELECT E'foo\\'bar'; COMMIT; -- '",
                "SET standard_conforming_strings = off"
            })
            {
                var rejected = SmokeCheck.Capture(() => MigrationCatalog.Create(providerName, [new MigrationDefinition { Number = 1, UpSql = sql }],
                    [new MigrationReleasePin { Release = "1.0.0", Number = 1 }]));
                if (!await StateAsync(provider, 0, 0)) return Fail("migration-transaction-control-lexing");
            }
            Pass(providerName, "migration-ambiguous-sql-rejected-before-database-changes");
            if (!await SmokeCheck.ThrowsAsync(() => runner.MigrateAsync("2.0.1")) || !await SmokeCheck.ThrowsAsync(() => runner.MigrateAsync("2.0.0-rc.1+build.8")) ||
                !await SmokeCheck.ThrowsAsync(() => runner.MigrateAsync("02.0.0")) || !await StateAsync(provider, 0, 0)) return Fail("migration-exact-pin");
            Pass(providerName, "migration-exact-pin-prerelease-build-missing-validation");
            var up = await runner.MigrateAsync("2.0.0-rc.1+build.7");
            var raised = up;
            if (raised.FromNumber != 0 || raised.ToNumber != 2 || raised.AppliedCount != 2 || !await StateAsync(provider, 2, 2)) return Fail("migration-up");
            var noop = await runner.MigrateAsync("2.0.0");
            var unchanged = noop;
            if (unchanged.AppliedCount != 0 || unchanged.FromNumber != 2 || unchanged.ToNumber != 2) return Fail("migration-noop");
            var down = await runner.MigrateAsync("1.1.0");
            var lowered = down;
            if (lowered.FromNumber != 2 || lowered.ToNumber != 1 || lowered.AppliedCount != 1 || !await StateAsync(provider, 1, 1)) return Fail("migration-down");
            if (!await SmokeCheck.CompletesAsync(() => runner.MigrateAsync("1.0.0")) || !await StateAsync(provider, 0, 0)) return Fail("migration-zero");
            Pass(providerName, "migration-compiled-up-down-noop-schema-history");

            await ResetAsync(provider);
            await using (var irreversible = CreateContainer(providerName, connectionString, MigrationFixtures.Create(providerName, irreversible: true)))
            {
                var irreversibleRunner = irreversible.GetRequiredService<IMigrationRunner>();
                if (!await SmokeCheck.CompletesAsync(() => irreversibleRunner.MigrateAsync("2.0.0")) || !await SmokeCheck.ThrowsAsync(() => irreversibleRunner.MigrateAsync("1.0.0")) || !await StateAsync(provider, 2, 2)) return Fail("migration-irreversible");
            }
            Pass(providerName, "migration-irreversible-downgrade-preflight-preserves-all-state");
            await ResetAsync(provider);
            await using (var recovery = CreateContainer(providerName, connectionString, MigrationFixtures.Create(providerName, failing: true)))
            {
                var recoveryRunner = recovery.GetRequiredService<IMigrationRunner>();
                var failure = await SmokeCheck.CaptureAsync(() => recoveryRunner.MigrateAsync("3.0.0"));
                if (providerName == "sqlite" && failure is not Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 1 }) return Fail("migration-sqlite-diagnostics");
                if (providerName == "postgresql" && failure is not Npgsql.PostgresException { SqlState: "42P01" }) return Fail("migration-postgres-diagnostics");
                if (!await StateAsync(provider, 0, 0)) return Fail("migration-failure-rollback");
                await ExecuteAsync(provider, "CREATE TABLE f002_migration_dependency (id BIGINT PRIMARY KEY)");
                if (!await SmokeCheck.CompletesAsync(() => recoveryRunner.MigrateAsync("3.0.0")) || !await StateAsync(provider, 3, 3)) return Fail("migration-retry");
            }
            Pass(providerName, "migration-failure-atomic-history-schema-rollback-retry");
            await ResetAsync(provider);
            var holder = (await provider.OpenConnectionAsync());
            await using (holder)
            await using (var transaction = await holder.BeginTransactionAsync())
            {
                await using (var hold = holder.CreateCommand())
                {
                    hold.Transaction = transaction;
                    hold.CommandText = providerName == "postgresql" ? "SELECT pg_advisory_xact_lock(564535375002)" : "CREATE TABLE f002_migration_lock (id BIGINT)";
                    await hold.ExecuteNonQueryAsync();
                    if (!await SmokeCheck.ThrowsAsync(() => runner.MigrateAsync("2.0.0"))) return Fail("migration-concurrent");
                    await transaction.RollbackAsync();
                }
            }
            if (!await StateAsync(provider, 0, 0) || !await SmokeCheck.CompletesAsync(() => runner.MigrateAsync("2.0.0"))) return Fail("migration-lock-release");
            Pass(providerName, "migration-database-lock-conflict-and-retry");
            await using (var prior = CreateContainer(providerName, connectionString, MigrationFixtures.CreatePriorRelease(providerName)))
            {
                if (!await SmokeCheck.ThrowsAsync(() => prior.GetRequiredService<IMigrationRunner>().MigrateAsync("1.0.0")) || !await StateAsync(provider, 2, 2)) return Fail("migration-missing-definition");
            }
            Pass(providerName, "migration-missing-applied-definition-rejected-before-ddl");
            await ExecuteAsync(provider, "UPDATE nyssa_repository_migrations SET fingerprint = 'changed' WHERE number = 1");
            if (!await SmokeCheck.ThrowsAsync(() => runner.MigrateAsync("2.0.0")) || !await StateAsync(provider, 2, 2)) return Fail("migration-history-corruption");
            Pass(providerName, "migration-history-mismatch-rejected-without-ddl");
            await ResetAsync(provider);
            if (providerName == "sqlite")
            {
                foreach (var corruption in new[] { "1.5", "NULL" })
                {
                    if (!await SmokeCheck.CompletesAsync(() => runner.MigrateAsync("2.0.0"))) return Fail("migration-history-fixture");
                    await ExecuteAsync(provider, "UPDATE nyssa_repository_migrations SET number = " + corruption + " WHERE number = 1");
                    if (!await SmokeCheck.ThrowsAsync(() => runner.MigrateAsync("1.0.0")) || !await StateAsync(provider, 2, 2)) return Fail("migration-history-storage-type");
                    await ResetAsync(provider);
                }
                Pass(providerName, "migration-sqlite-history-storage-types-rejected-before-ddl");
            }
            if (!await SmokeCheck.ThrowsAsync(() => runner.MigrateAsync("2.0.0", new CancellationToken(true))) || !await StateAsync(provider, 0, 0)) return Fail("migration-precancellation");
            Pass(providerName, "migration-precancellation-preserves-state");
            if (providerName == "postgresql")
            {
                var nonstandard = new Npgsql.NpgsqlConnectionStringBuilder(connectionString) { Options = "-c standard_conforming_strings=off" };
                await using (var incompatible = CreateContainer(providerName, nonstandard.ConnectionString, MigrationFixtures.Create(providerName)))
                {
                    if (!await SmokeCheck.ThrowsAsync(() => incompatible.GetRequiredService<IMigrationRunner>().MigrateAsync("2.0.0")) || !await StateAsync(provider, 0, 0)) return Fail("migration-nonstandard-quoting");
                    Pass(providerName, "migration-postgresql-nonstandard-quoting-rejected-before-ddl");
                }
            }
            if (providerName == "sqlite")
            {
                await using (var triggerContainer = CreateContainer(providerName, connectionString, MigrationFixtures.CreateTrigger()))
                {
                    var triggerRunner = triggerContainer.GetRequiredService<IMigrationRunner>();
                    if (!await SmokeCheck.CompletesAsync(() => triggerRunner.MigrateAsync("2.0.0")) || !await StateAsync(provider, 2, 1)) return Fail("migration-trigger-up");
                    var observer = (await provider.OpenConnectionAsync());
                    await using (observer)
                    {
                        await using (var query = observer.CreateCommand())
                        {
                            query.CommandText = "SELECT id FROM f002_migration_two";
                            if (Convert.ToInt64(await query.ExecuteScalarAsync()) != 73) return Fail("migration-trigger-executed");
                        }
                    }
                    if (!await SmokeCheck.CompletesAsync(() => triggerRunner.MigrateAsync("1.0.0")) || !await StateAsync(provider, 0, 0)) return Fail("migration-trigger-down");
                    Pass(providerName, "migration-sqlite-transactional-trigger-up-execute-down");
                }
            }
            return 0;
        }
    }

    internal static ServiceProvider CreateContainer(string provider, string connectionString, MigrationCatalog catalog)
    {
        var services = new ServiceCollection();
        services.AddNyssaRepository(new RepositoryConfiguration { DatabaseProvider = provider, ConnectionString = connectionString }, migrations: [catalog]);
        return services.BuildServiceProvider();
    }

    internal static Task ResetAsync(IRepositoryProvider provider) => ExecuteAsync(provider,
        "DROP TABLE IF EXISTS f002_migration_one; DROP TABLE IF EXISTS f002_migration_two; DROP TABLE IF EXISTS f002_migration_three; DROP TABLE IF EXISTS f002_migration_dependency; DROP TABLE IF EXISTS f002_migration_lock; DROP TABLE IF EXISTS nyssa_repository_migrations");

    internal static async Task<bool> StateAsync(IRepositoryProvider provider, int tables, int history)
    {
        var connection = (await provider.OpenConnectionAsync());
        await using (connection)
        {
            await using (var query = connection.CreateCommand())
            {
                query.CommandText = provider.DatabaseProvider == "sqlite"
                    ? "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('f002_migration_one', 'f002_migration_two', 'f002_migration_three')"
                    : "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('f002_migration_one', 'f002_migration_two', 'f002_migration_three')";
                if (Convert.ToInt64(await query.ExecuteScalarAsync()) != tables) return false;
                query.CommandText = provider.DatabaseProvider == "sqlite"
                    ? "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'nyssa_repository_migrations'"
                    : "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'nyssa_repository_migrations'";
                if (Convert.ToInt64(await query.ExecuteScalarAsync()) == 0) return history == 0;
                query.CommandText = "SELECT COUNT(*) FROM nyssa_repository_migrations";
                return Convert.ToInt64(await query.ExecuteScalarAsync()) == history;
            }
        }
    }

    private static async Task ExecuteAsync(IRepositoryProvider provider, string sql)
    {
        var connection = (await provider.OpenConnectionAsync());
        await using (connection)
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.CommandTimeout = 10;
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private static void Pass(string provider, string scenario) => Console.WriteLine($"PASS {provider} {scenario}");
    private static int Fail(string scenario) { Console.Error.WriteLine($"FAIL {scenario}"); return 1; }
}
