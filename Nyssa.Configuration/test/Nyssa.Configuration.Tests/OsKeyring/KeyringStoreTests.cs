using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.OsKeyring;

namespace NyssaAI.Configuration.Tests.OsKeyring;

/// <summary>
/// The contract every platform adapter owes <see cref="IKeyring"/>, asserted against the real
/// store rather than a fake.
/// </summary>
/// <remarks>
/// <para>
/// A fake keyring can only re-state the interface. Everything that actually goes wrong in these
/// adapters is on the other side of the P/Invoke: a blob decoded with the wrong encoding, a length
/// in characters where the API wants bytes, a handle released twice, an error code mistaken for an
/// absent secret. None of that shows up until a real store answers.
/// </para>
/// <para>
/// The class is abstract so xUnit does not collect it; each platform derives from it, supplies its
/// adapter. Cleanup uses the same public deletion contract as callers.
/// </para>
/// </remarks>
public abstract class KeyringStoreTests : IDisposable
{
    private readonly List<string> _written = [];

    /// <summary>Creates the adapter under test.</summary>
    protected abstract IKeyring Create();

    /// <summary>Ends the test as skipped when this platform is not the one running.</summary>
    protected abstract void RequirePlatform();

    /// <summary>Releases every credential the test filed, whether it passed or failed.</summary>
    public void Dispose()
    {
        // Only keys handed out by NewKey are listed, so a test that skipped before writing anything
        // deletes nothing -- which is what keeps this safe to run on a platform it does not target.
        foreach (var key in _written)
        {
            var deleted = Create().Delete(RealKeyring.Service, key);
            Assert.True(deleted.IsSuccess, deleted.ToString());
        }

        _written.Clear();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Get_ForAKeyNothingWasFiledUnder_SucceedsCarryingNull()
    {
        var keyring = Ready();

        var found = keyring.Get(RealKeyring.Service, NewKey());

        // The distinction BootstrapProviderKeyring rests on: an absent secret is a success carrying
        // null, never a failure. Reporting absence as a failure would stop a bootstrap that should
        // have gone on to create the secret.
        Assert.True(found.IsSuccess, found.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.Null(found.Value);
    }

    [Fact]
    public void Delete_AStoredSecret_RemovesOnlyTheAddressedKey()
    {
        var keyring = Ready();
        var key = NewKey();
        var neighbour = NewKey();
        Assert.True(keyring.Set(RealKeyring.Service, key, "remove-me").IsSuccess);
        Assert.True(keyring.Set(RealKeyring.Service, neighbour, "keep-me").IsSuccess);

        var deleted = keyring.Delete(RealKeyring.Service, key);

        Assert.True(deleted.IsSuccess, deleted.ToString());
        Assert.Null(Read(keyring, key));
        Assert.Equal("keep-me", Read(keyring, neighbour));
    }

    [Fact]
    public void Delete_AStoredSecret_PreservesTheSameKeyInAnotherService()
    {
        var keyring = Ready();
        var key = NewKey();
        var otherService = RealKeyring.Service + ".neighbour";
        try
        {
            Assert.True(keyring.Set(RealKeyring.Service, key, "remove-me").IsSuccess);
            Assert.True(keyring.Set(otherService, key, "keep-me").IsSuccess);

            var deleted = keyring.Delete(RealKeyring.Service, key);

            Assert.True(deleted.IsSuccess, deleted.ToString());
            var preserved = keyring.Get(otherService, key);
            Assert.True(preserved.IsSuccess, preserved.ToString());
            Assert.Equal("keep-me", preserved.Value);
        }
        finally
        {
            var deleted = keyring.Delete(otherService, key);
            Assert.True(deleted.IsSuccess, deleted.ToString());
        }
    }

    [Fact]
    public void Delete_AnAbsentOrAlreadyDeletedSecret_Succeeds()
    {
        var keyring = Ready();
        var key = NewKey();
        Assert.True(keyring.Delete(RealKeyring.Service, key).IsSuccess);
        Assert.True(keyring.Set(RealKeyring.Service, key, "remove-me").IsSuccess);
        Assert.True(keyring.Delete(RealKeyring.Service, key).IsSuccess);

        var deletedAgain = keyring.Delete(RealKeyring.Service, key);

        Assert.True(deletedAgain.IsSuccess, deletedAgain.ToString());
        Assert.Null(Read(keyring, key));
    }

    [Fact]
    public void Set_ThenGet_ReturnsTheSecret()
    {
        var keyring = Ready();
        var key = NewKey();

        var stored = keyring.Set(RealKeyring.Service, key, "correct horse battery staple");

        Assert.True(stored.IsSuccess, stored.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.Equal("correct horse battery staple", Read(keyring, key));
    }

    [Fact]
    public void Set_Twice_ReplacesTheValueRatherThanFilingASecond()
    {
        var keyring = Ready();
        var key = NewKey();

        Assert.True(keyring.Set(RealKeyring.Service, key, "first").IsSuccess);
        Assert.True(keyring.Set(RealKeyring.Service, key, "second").IsSuccess);

        // A rotation writes over the same address. If the store filed a second item instead, the
        // read would keep answering with the old secret long after it was replaced.
        Assert.Equal("second", Read(keyring, key));
    }

    [Theory]
    // Text outside ASCII is where a UTF-16 blob read as UTF-8, or the reverse, first shows itself.
    [InlineData("pässwörd")]
    [InlineData("пароль")]
    [InlineData("密码")]
    // Beyond the BMP: a surrogate pair, which a length-in-characters mistake truncates.
    [InlineData("key \U0001F510 material")]
    // Bytes that mean something to a shell or a query string but nothing to the store.
    [InlineData("a=b&c=d;e f\\g\"h'i")]
    public void Set_ThenGet_RoundTripsTextExactly(string secret)
    {
        var keyring = Ready();
        var key = NewKey();

        Assert.True(keyring.Set(RealKeyring.Service, key, secret).IsSuccess);

        Assert.Equal(secret, Read(keyring, key));
    }

    [Fact]
    public void Set_ThenGet_RoundTripsASecretLargerThanOneBuffer()
    {
        var keyring = Ready();
        var key = NewKey();
        // 1000 characters is 2000 bytes as UTF-16, inside the Credential Manager's 2560-byte blob
        // limit and well past any single-buffer assumption. A size computed in characters where the
        // API counts bytes is exactly half right, which only a long secret makes visible.
        var secret = string.Concat(Enumerable.Range(0, 100).Select(i => $"{i:D9}-"));

        var stored = keyring.Set(RealKeyring.Service, key, secret);
        Assert.True(stored.IsSuccess, stored.Errors.FirstOrDefault()?.TechnicalMessage);

        Assert.Equal(secret, Read(keyring, key));
    }

    [Fact]
    public void Set_ThenGet_RoundTripsAnEmptySecret()
    {
        var keyring = Ready();
        var key = NewKey();

        Assert.True(keyring.Set(RealKeyring.Service, key, string.Empty).IsSuccess);

        // An empty secret is still a stored secret. It must read back as the empty string, because
        // null is the answer reserved for "nothing is filed here" and a caller branches on it.
        var found = keyring.Get(RealKeyring.Service, key);
        Assert.True(found.IsSuccess, found.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.Equal(string.Empty, found.Value);
    }

    [Fact]
    public void Get_AnswersForOneKeyWithoutSeeingAnother()
    {
        var keyring = Ready();
        var first = NewKey();
        var second = NewKey();
        Assert.True(keyring.Set(RealKeyring.Service, first, "first-secret").IsSuccess);
        Assert.True(keyring.Set(RealKeyring.Service, second, "second-secret").IsSuccess);

        // The two halves of the address both have to reach the store. An adapter that composed only
        // the service would pass every test above and hand out the wrong secret here.
        Assert.Equal("first-secret", Read(keyring, first));
        Assert.Equal("second-secret", Read(keyring, second));
    }

    [Fact]
    public void Get_ForAKeyThatWasNeverFiled_DoesNotSeeANeighbour()
    {
        var keyring = Ready();
        var filed = NewKey();
        Assert.True(keyring.Set(RealKeyring.Service, filed, "filed").IsSuccess);

        // A prefix match would find it; the store is asked for an exact address.
        var found = keyring.Get(RealKeyring.Service, filed + "-suffix");

        Assert.True(found.IsSuccess, found.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.Null(found.Value);
    }

    [Fact]
    public void Get_RepeatedManyTimes_KeepsAnsweringWithTheSameSecret()
    {
        var keyring = Ready();
        var key = NewKey();
        Assert.True(keyring.Set(RealKeyring.Service, key, "stable").IsSuccess);

        // Each read allocates a native credential and releases it. A release that was missed, or
        // done twice, does not fail the first read -- it fails the hundredth, or corrupts the heap.
        // Both hits and misses are exercised, because they leave the handle in different states.
        for (var i = 0; i < 200; i++)
        {
            Assert.Equal("stable", Read(keyring, key));

            var absent = keyring.Get(RealKeyring.Service, key + "-absent");
            Assert.True(absent.IsSuccess, absent.Errors.FirstOrDefault()?.TechnicalMessage);
            Assert.Null(absent.Value);
        }
    }

    [Fact]
    public void Set_RepeatedManyTimes_KeepsSucceeding()
    {
        var keyring = Ready();
        var key = NewKey();

        // Every write allocates two unmanaged buffers and frees them in a finally. A missed free
        // shows up as growth over a run, never as a failed assertion on one call.
        for (var i = 0; i < 200; i++)
        {
            var stored = keyring.Set(RealKeyring.Service, key, $"rotation-{i}");
            Assert.True(stored.IsSuccess, stored.Errors.FirstOrDefault()?.TechnicalMessage);
        }

        Assert.Equal("rotation-199", Read(keyring, key));
    }

    [Theory]
    [InlineData("", "key")]
    [InlineData("  ", "key")]
    [InlineData("service", "")]
    [InlineData("service", "\t")]
    [InlineData("serv ice", "key")]
    [InlineData("service", "ke\ny")]
    public void AnAddressTheStoreCannotBeAskedAbout_IsRefusedBeforeTheNativeCall(string service, string key)
    {
        var keyring = Ready();

        // Refused on all three verbs and by the same rule, so a write cannot file a secret at an address
        // the matching read would refuse to compose.
        var found = keyring.Get(service, key);
        var stored = keyring.Set(service, key, "unreachable");
        var deleted = keyring.Delete(service, key);

        Assert.True(found.IsFailure);
        Assert.True(stored.IsFailure);
        Assert.True(deleted.IsFailure);
        Assert.Equal(NyssaAI.Results.ErrorCategory.Validation, Assert.Single(deleted.Errors).Category);
        Assert.Equal(NyssaAI.Results.ErrorCategory.Validation, Assert.Single(found.Errors).Category);
        Assert.Equal(NyssaAI.Results.ErrorCategory.Validation, Assert.Single(stored.Errors).Category);
    }

    [Fact]
    public void ManagedSecret_GetOrCreate_CreatesOnceAndReturnsTheSameMaterialAfter()
    {
        var keyring = Ready();
        var key = NewKey();

        var created = KeyringManagedSecret.GetOrCreate(RealKeyring.Service, key, 32, keyring);
        var reread = KeyringManagedSecret.GetOrCreate(RealKeyring.Service, key, 32, keyring);

        // This is the path every caller actually takes. The encoding it stores has to survive the
        // store's own round trip: a secret that comes back re-encoded decodes to different bytes,
        // and a key that changes between runs silently invalidates everything sealed under it.
        Assert.True(created.IsSuccess, created.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.True(reread.IsSuccess, reread.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.Equal(32, created.Value.Material.Length);
        Assert.Equal(created.Value.Material, reread.Value.Material);
    }

    [Fact]
    public void KeyringDefault_Create_ReturnsTheAdapterForThisPlatform()
    {
        RequirePlatform();

        // The whole suite tests an adapter the production path only reaches through this factory.
        Assert.IsType(Create().GetType(), KeyringDefault.Create());
    }

    /// <summary>Skips unless this platform is running and the operator opted in, then builds the adapter.</summary>
    protected IKeyring Ready()
    {
        RequirePlatform();
        RealKeyring.RequireOptIn();
        return Create();
    }

    /// <summary>Reserves a key unique to this run and registers it for deletion.</summary>
    protected string NewKey()
    {
        var key = $"manual-{Guid.NewGuid():N}";
        _written.Add(key);
        return key;
    }

    private static string? Read(IKeyring keyring, string key)
    {
        var found = keyring.Get(RealKeyring.Service, key);
        Assert.True(found.IsSuccess, found.Errors.FirstOrDefault()?.TechnicalMessage);
        return found.Value;
    }
}
