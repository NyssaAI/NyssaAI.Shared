using Xunit;

namespace NyssaAI.Results.Tests;

public sealed class ResultUnwrapTests
{
    [Fact]
    public void Unwrap_Success_ReturnsOriginalValueIncludingNull()
    {
        var value = new object();
        Assert.Same(value, Result<object>.Success(value).Unwrap());
        Assert.Null(Result<string?>.Success(null).Unwrap());
        Assert.Equal(42, Result<int>.Success(42).Unwrap());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unwrap_Failure_ThrowsWithEveryError(bool asynchronous)
    {
        var errors = new[]
        {
            Error.FromMessages("Invalid task", "private-detail", ErrorCategory.Validation),
            Error.FromMessages("Already exists", "database-detail", ErrorCategory.Conflict)
        };
        var result = Result<int>.Failure(errors);
        var exception = asynchronous
            ? await Assert.ThrowsAsync<ResultException>(() => Task.FromResult(result).UnwrapAsync())
            : Assert.Throws<ResultException>(() => result.Unwrap());

        Assert.Equal(errors, exception.Errors);
        Assert.DoesNotContain("private-detail", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("database-detail", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnwrapAsync_PendingSuccess_AwaitsWithoutBlocking()
    {
        var completion = new TaskCompletionSource<Result<object>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unwrapped = completion.Task.UnwrapAsync();
        Assert.False(unwrapped.IsCompleted);

        var value = new object();
        completion.SetResult(Result<object>.Success(value));
        Assert.Same(value, await unwrapped);
        Assert.Null(await Task.FromResult(Result<string?>.Success(null)).UnwrapAsync());
    }

    [Fact]
    public async Task UnwrapAsync_FaultedTask_PreservesOriginalException()
    {
        var failure = new InvalidOperationException("Author failure");
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Task.FromException<Result<int>>(failure).UnwrapAsync());
        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task UnwrapAsync_CanceledTask_PreservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var unwrapped = Task.FromCanceled<Result<int>>(cancellation.Token).UnwrapAsync();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unwrapped);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(unwrapped.IsCanceled);
    }
}
