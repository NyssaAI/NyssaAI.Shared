using NyssaAI.Configuration.OsKeyring;
using NyssaAI.Results;
using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Tests.OsKeyring;

public sealed class KeyringManagedSecretTests
{
    [Fact]
    public void GetOrCreate_CreatesAndFilesMaterialWhenTheStoreHoldsNone()
    {
        var keyring = new FakeKeyring();

        var created = KeyringManagedSecret.GetOrCreate("checkout-config", "aesLocal.key", 32, keyring);

        Assert.True(created.IsSuccess, created.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.True(created.Value.Created);
        Assert.Equal(32, created.Value.Material.Length);
        Assert.Equal(Convert.ToBase64String(created.Value.Material), keyring.Stored["checkout-config/aesLocal.key"]);
    }

    [Fact]
    public void GetOrCreate_ReturnsTheFiledMaterialAndWritesNothingOnASecondRun()
    {
        var keyring = new FakeKeyring();
        var first = KeyringManagedSecret.GetOrCreate("checkout-config", "aesLocal.key", 32, keyring);
        keyring.Writes = 0;

        var second = KeyringManagedSecret.GetOrCreate("checkout-config", "aesLocal.key", 32, keyring);

        Assert.True(second.IsSuccess, second.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.False(second.Value.Created);
        Assert.Equal(first.Value.Material, second.Value.Material);
        Assert.Equal(0, keyring.Writes);
    }

    [Fact]
    public void Get_ReturnsNullAndWritesNothingWhenTheStoreHoldsNone()
    {
        // The administered-machine path: absence is the administrator's to fix, never this process's.
        var keyring = new FakeKeyring();

        var read = KeyringManagedSecret.Get("checkout-config", "aesLocal.key", 32, keyring);

        Assert.True(read.IsSuccess, read.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.Null(read.Value);
        Assert.Equal(0, keyring.Writes);
    }

    [Fact]
    public void Get_ReturnsTheFiledMaterial()
    {
        var keyring = new FakeKeyring();
        var placed = new byte[32];
        placed[0] = 7;
        keyring.Stored["checkout-config/aesLocal.key"] = Convert.ToBase64String(placed);

        var read = KeyringManagedSecret.Get("checkout-config", "aesLocal.key", 32, keyring);

        Assert.True(read.IsSuccess, read.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.Equal(placed, read.Value);
    }

    [Fact]
    public void Get_ReportsAStoredValueThatIsNotMaterialOfTheRequestedLength()
    {
        var keyring = new FakeKeyring();
        keyring.Stored["checkout-config/aesLocal.key"] = Convert.ToBase64String(new byte[16]);

        var read = KeyringManagedSecret.Get("checkout-config", "aesLocal.key", 32, keyring);

        Assert.True(read.IsFailure);
        Assert.Equal(0, keyring.Writes);
    }

    [Fact]
    public void GetOrCreate_RefusesAndCreatesNothingWhenTheStoreCannotBeRead()
    {
        var keyring = new FakeKeyring { ReadFails = true };

        var read = KeyringManagedSecret.GetOrCreate("checkout-config", "aesLocal.key", 32, keyring);

        Assert.True(read.IsFailure);
        Assert.Equal(0, keyring.Writes);
    }

    [Fact]
    public void GetOrCreate_ReportsAStoredValueThatIsNotMaterialOfTheRequestedLength()
    {
        var keyring = new FakeKeyring();
        keyring.Stored["checkout-config/aesLocal.key"] = Convert.ToBase64String(new byte[16]);

        var read = KeyringManagedSecret.GetOrCreate("checkout-config", "aesLocal.key", 32, keyring);

        Assert.True(read.IsFailure);
        Assert.DoesNotContain("aesLocal.key", read.Errors[0].UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void GetOrCreate_ReportsAStoreThatRefusesTheWrite()
    {
        var keyring = new FakeKeyring { WriteFails = true };

        var created = KeyringManagedSecret.GetOrCreate("checkout-config", "aesLocal.key", 32, keyring);

        Assert.True(created.IsFailure);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(128)]
    public void GetOrCreate_RefusesALengthOutsideTheSupportedRange(int length)
    {
        var keyring = new FakeKeyring();

        var created = KeyringManagedSecret.GetOrCreate("checkout-config", "aesLocal.key", length, keyring);

        Assert.True(created.IsFailure);
        Assert.Equal(0, keyring.Writes);
    }

    private sealed class FakeKeyring : IKeyring
    {
        public Dictionary<string, string> Stored { get; } = new(StringComparer.Ordinal);

        public int Writes { get; set; }

        public bool ReadFails { get; init; }

        public bool WriteFails { get; init; }

        public Result<string?> Get(string service, string key) =>
            ReadFails
                ? Result<string?>.Failure(Unavailable)
                : Result<string?>.Success(Stored.GetValueOrDefault($"{service}/{key}"));

        public Result Set(string service, string key, string secret)
        {
            Writes++;
            if (WriteFails)
                return Result.Failure(Unavailable);

            Stored[$"{service}/{key}"] = secret;
            return Result.Success();
        }

        public Result Delete(string service, string key)
        {
            Stored.Remove($"{service}/{key}");
            return Result.Success();
        }

        private static Error Unavailable => Error.FromMessages(
            "The credential store could not be reached.",
            "keyring: test double failure",
            ErrorCategory.Unavailable);
    }
}
