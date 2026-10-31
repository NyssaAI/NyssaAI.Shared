using NyssaAI.Configuration.Contracts;
using System.Diagnostics;
using NyssaAI.Results;

using NyssaAI.Configuration.Values;
using NyssaAI.Configuration.Tests.Support;

namespace NyssaAI.Configuration.Tests.Values;

/// <summary>
/// The cross-process namespace lock, and its bound.
/// </summary>
/// <remarks>
/// The wait used to be a <c>while (true)</c> retry with no deadline, so a lock another process
/// never released hung the caller with no error and no timeout -- indistinguishable, from the
/// outside, from the machine being slow.
/// </remarks>
public sealed class ConfigurationDocumentLockTests
{
    [Fact]
    public async Task ReadAsync_WhenAnotherHolderKeepsTheLock_FailsWithinTheTimeout()
    {
        using var directory = new TemporaryDirectory();
        var documentPath = Path.Combine(directory.Path, "service.json");
        var timeout = TimeSpan.FromMilliseconds(300);

        // Held open for the whole test, standing in for another process mid read-modify-write.
        await using var holder = new FileStream(
            documentPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var document = ConfigurationDocumentLocalFile.Create(documentPath, timeout).Value;

        var started = Stopwatch.GetTimestamp();
        var read = await document.ReadAsync(TestContext.Current.CancellationToken);
        var waited = Stopwatch.GetElapsedTime(started);

        Assert.True(read.IsFailure);

        var error = Assert.Single(read.Errors);
        // Unavailable, not Unexpected: the holder is another writer finishing, so a retry can win.
        Assert.Equal(ErrorCategory.Unavailable, error.Category);
        Assert.Contains("could not take the lock", error.TechnicalMessage, StringComparison.Ordinal);

        Assert.True(waited >= timeout, $"returned after {waited.TotalMilliseconds:0}ms, before the timeout");
        Assert.True(waited < timeout + TimeSpan.FromSeconds(5), $"overshot the timeout badly at {waited.TotalMilliseconds:0}ms");
    }

    [Fact]
    public async Task ModifyAsync_WhenAnotherHolderKeepsTheLock_FailsRatherThanHanging()
    {
        using var directory = new TemporaryDirectory();
        var documentPath = Path.Combine(directory.Path, "service.json");

        await using var holder = new FileStream(
            documentPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var document = ConfigurationDocumentLocalFile.Create(documentPath, TimeSpan.FromMilliseconds(200)).Value;

        var changed = await document.ModifyAsync(
            _ => ValueTask.FromResult(Result.Success()),
            TestContext.Current.CancellationToken);

        Assert.True(changed.IsFailure);
        Assert.Equal(ErrorCategory.Unavailable, Assert.Single(changed.Errors).Category);
    }

    [Fact]
    public async Task ReadAsync_OnceTheHolderReleases_Succeeds()
    {
        // The bound must not have turned the lock into a probe: an uncontended read still works,
        // and a contended one succeeds as soon as the holder lets go.
        using var directory = new TemporaryDirectory();
        var documentPath = Path.Combine(directory.Path, "service.json");

        var holder = new FileStream(
            documentPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var document = ConfigurationDocumentLocalFile.Create(documentPath, TimeSpan.FromSeconds(10)).Value;
        var reading = document.ReadAsync(TestContext.Current.CancellationToken).AsTask();

        await holder.DisposeAsync();

        var read = await reading;
        Assert.True(read.IsSuccess);
        Assert.Empty(read.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithANonPositiveTimeout_IsRefused(int seconds)
    {
        var created = ConfigurationDocumentLocalFile.Create("service.json", TimeSpan.FromSeconds(seconds));

        Assert.True(created.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(created.Errors).Category);
    }

    [Fact]
    public async Task ReadAsync_WhenCanceledWhileWaiting_ReturnsCanceled()
    {
        using var directory = new TemporaryDirectory();
        var documentPath = Path.Combine(directory.Path, "service.json");
        await using var holder = new FileStream(
            documentPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var canceled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var document = ConfigurationDocumentLocalFile.Create(documentPath, TimeSpan.FromSeconds(5)).Value;

        var read = await document.ReadAsync(canceled.Token);

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Canceled, Assert.Single(read.Errors).Category);
    }

    [Fact]
    public async Task ModifyAsync_WhenChangeFails_DoesNotPublishTheMutation()
    {
        using var directory = new TemporaryDirectory();
        var documentPath = Path.Combine(directory.Path, "service.json");
        var document = new ConfigurationDocumentLocalFile(documentPath);

        var changed = await document.ModifyAsync(
            entries =>
            {
                entries["key"] = new ConfigurationValue { Value = "value" };
                return ValueTask.FromResult(Result.Failure(
                    Error.FromMessages("Refused", "test refusal", ErrorCategory.Validation)));
            },
            TestContext.Current.CancellationToken);

        Assert.True(changed.IsFailure);
        Assert.False(File.Exists(documentPath));
    }

    [Fact]
    public async Task ModifyAsync_WithConcurrentDocumentInstances_PreservesBothChanges()
    {
        using var directory = new TemporaryDirectory();
        var documentPath = Path.Combine(directory.Path, "service.json");
        var first = new ConfigurationDocumentLocalFile(documentPath);
        var second = new ConfigurationDocumentLocalFile(documentPath);

        await Task.WhenAll(
            AddAsync(first, "first"),
            AddAsync(second, "second"));

        var read = await first.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(read.IsSuccess);
        Assert.Equal(2, read.Value.Count);
    }

    private static async Task AddAsync(ConfigurationDocumentLocalFile document, string key)
    {
        var result = await document.ModifyAsync(
            entries =>
            {
                entries[key] = new ConfigurationValue { Value = key };
                return ValueTask.FromResult(Result.Success());
            },
            TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
    }
}
