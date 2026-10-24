using NyssaAI.Repository.Migrations;
using Xunit;

namespace NyssaAI.Repository.Tests.Migrations;

public sealed class MigrationCatalogTests
{
    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("1.0.0-rc.1+build.05", true)]
    [InlineData("1.0.0+001", true)]
    [InlineData("1.0", false)]
    [InlineData("01.0.0", false)]
    [InlineData("1.0.0-01", false)]
    [InlineData("1.0.0-", false)]
    [InlineData("1.0.0+", false)]
    [InlineData("v1.0.0", false)]
    public void Create_ReleaseSyntax_ValidatesSemVer(string release, bool valid)
    {
        if (valid)
            Assert.NotNull(MigrationCatalog.Create("sqlite", [], [new MigrationReleasePin { Release = release, Number = 0 }]));
        else
            Assert.Throws<ArgumentException>(() => MigrationCatalog.Create("sqlite", [], [new MigrationReleasePin { Release = release, Number = 0 }]));
    }

    [Theory]
    [InlineData("COMMIT")]
    [InlineData("CREATE TABLE sample(id INT); /* leading */ END")]
    [InlineData("-- leading\nBEGIN")]
    [InlineData("PRAGMA journal_mode=WAL")]
    [InlineData("VACUUM")]
    [InlineData("/* unclosed")]
    [InlineData("CREATE TRIGGER sample AFTER INSERT ON one BEGIN INSERT INTO two VALUES (NEW.id); END; COMMIT")]
    [InlineData("CREATE TEMP TRIGGER sample AFTER INSERT ON one BEGIN INSERT INTO two VALUES (NEW.id); END; /* next */ ROLLBACK")]
    [InlineData("CREATE TABLE sample(trigger TEXT); END")]
    [InlineData("CREATE TABLE example(id); /* outer /* inner */ COMMIT; -- */")]
    [InlineData("CREATE TABLE example(id int); SELECT E'foo\\'bar'; COMMIT; -- '")]
    [InlineData("SELECT e'line\\n'")]
    [InlineData("SET standard_conforming_strings = off")]
    [InlineData("RESET ALL")]
    public void Create_TransactionControl_RejectsBeforeDatabaseChanges(string sql)
    {
        var result = Assert.Throws<ArgumentException>(() => MigrationCatalog.Create("sqlite", [new() { Number = 1, UpSql = sql }], [new() { Release = "1.0.0", Number = 1 }]));

    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Create_NoncontiguousStep_RejectsCatalog(long number)
    {
        var result = Assert.Throws<ArgumentException>(() => MigrationCatalog.Create("sqlite", [new() { Number = number, UpSql = "CREATE TABLE sample(id INT)" }], [new() { Release = "1.0.0", Number = 0 }]));

    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    public void Create_DuplicateOrUnreachablePin_RejectsCatalog(string provider)
    {
        Assert.Throws<ArgumentException>(() => MigrationCatalog.Create(provider, [], [new() { Release = "1.0.0", Number = 1 }]));
        Assert.Throws<ArgumentException>(() => MigrationCatalog.Create(provider, [], [new() { Release = "1.0.0", Number = 0 }, new() { Release = "1.0.0", Number = 0 }]));
        Assert.Throws<ArgumentException>(() => MigrationCatalog.Create(provider, [new() { Number = 1, UpSql = "SELECT 1" }, new() { Number = 1, UpSql = "SELECT 2" }], [new() { Release = "1.0.0", Number = 1 }]));
    }

    [Fact]
    public void Create_CommandTimeoutSeconds_ValidatesAndStoresCatalogAndStepTimeouts()
    {
        var catalog = MigrationCatalog.Create(
            "sqlite",
            [
                new() { Number = 1, UpSql = "SELECT 1", CommandTimeoutSeconds = 0 },
                new() { Number = 2, UpSql = "SELECT 2", CommandTimeoutSeconds = 120 }
            ],
            [new() { Release = "1.0.0", Number = 2 }],
            commandTimeoutSeconds: 60);

        Assert.Equal(60, catalog.CommandTimeoutSeconds);
        Assert.Equal(0, catalog.Definitions[0].CommandTimeoutSeconds);
        Assert.Equal(120, catalog.Definitions[1].CommandTimeoutSeconds);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MigrationCatalog.Create("sqlite", [], [new() { Release = "1.0.0", Number = 0 }], commandTimeoutSeconds: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MigrationCatalog.Create("sqlite", [new() { Number = 1, UpSql = "SELECT 1", CommandTimeoutSeconds = -1 }], [new() { Release = "1.0.0", Number = 1 }]));
    }
}
