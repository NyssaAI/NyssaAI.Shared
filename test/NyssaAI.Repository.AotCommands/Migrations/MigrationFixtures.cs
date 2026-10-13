using NyssaAI.Repository.Migrations;

namespace NyssaAI.Repository.AotCommands.Migrations;

/// <summary>Compiled migration fixtures consumed by the native application.</summary>
public static class MigrationFixtures
{
    /// <summary>Represents an older artifact that lacks the second applied migration definition.</summary>
    public static MigrationCatalog CreatePriorRelease(string provider) => MigrationCatalog.Create(provider,
    [new MigrationDefinition { Number = 1, UpSql = "CREATE TABLE f002_migration_one (id BIGINT PRIMARY KEY)", DownSql = "DROP TABLE f002_migration_one" }],
    [new MigrationReleasePin { Release = "1.0.0", Number = 0 }, new MigrationReleasePin { Release = "1.1.0", Number = 1 }]).ThrowIfError();

    /// <summary>Creates SQLite trigger DDL whose internal END must not end the runner's transaction.</summary>
    public static MigrationCatalog CreateTrigger() => MigrationCatalog.Create("sqlite",
    [
        new MigrationDefinition
        {
            Number = 1,
            UpSql = "CREATE TABLE f002_migration_one (id BIGINT PRIMARY KEY); CREATE TABLE f002_migration_two (id BIGINT PRIMARY KEY); CREATE TRIGGER f002_migration_insert AFTER INSERT ON f002_migration_one BEGIN INSERT INTO f002_migration_two VALUES (NEW.id); END; INSERT INTO f002_migration_one VALUES (73)",
            DownSql = "DROP TABLE f002_migration_one; DROP TABLE f002_migration_two"
        }
    ],
    [new MigrationReleasePin { Release = "1.0.0", Number = 0 }, new MigrationReleasePin { Release = "2.0.0", Number = 1 }]).ThrowIfError();

    /// <summary>Creates a fixed two-step schema and exact release pins.</summary>
    public static MigrationCatalog Create(string provider, bool irreversible = false, bool failing = false)
    {
        var definitions = new List<MigrationDefinition>
        {
            new() { Number = 1, UpSql = "CREATE TABLE f002_migration_one (id BIGINT PRIMARY KEY)", DownSql = "DROP TABLE f002_migration_one" },
            new() { Number = 2, UpSql = "CREATE TABLE f002_migration_two (id BIGINT PRIMARY KEY)", DownSql = irreversible ? null : "DROP TABLE f002_migration_two" }
        };
        if (failing)
            definitions.Add(new MigrationDefinition
            {
                Number = 3,
                UpSql = "CREATE TABLE f002_migration_three (id BIGINT PRIMARY KEY); INSERT INTO f002_migration_dependency (id) VALUES (1)",
                DownSql = "DROP TABLE f002_migration_three"
            });
        var releases = new List<MigrationReleasePin>
        {
            new() { Release = "1.0.0", Number = 0 },
            new() { Release = "1.1.0", Number = 1 },
            new() { Release = "2.0.0", Number = 2 },
            new() { Release = "2.0.0-rc.1+build.7", Number = 2 }
        };
        if (failing) releases.Add(new MigrationReleasePin { Release = "3.0.0", Number = 3 });
        return MigrationCatalog.Create(provider, definitions, releases).ThrowIfError();
    }
}
