using Xunit;

namespace NyssaAI.Results.Tests;

// A TaskCanceledException is also what HttpClient throws on a timeout and what a linked timeout
// source produces; neither was asked for by the caller, and the caller's reasonable response
// (retry) is the opposite of the response to a cancellation it requested. These pin the evidence
// the translation reads: an inner TimeoutException, and whether a token actually fired.
public sealed class ResultCancellationTests
{
    [Fact]
    public void Try_WhenATaskCanceledExceptionCarriesATimeout_ReportsUnavailableNotCanceled()
    {
        var result = Result.Try(() => throw new TaskCanceledException("t", new TimeoutException()));

        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Unavailable, error.Category);
        Assert.Equal(Error.TimedOutUserMessage, error.UserMessage);
        Assert.Contains("TimeoutException", error.TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Try_WhenTheExceptionsTokenWasNeverCancelled_ReportsUnavailable()
    {
        // Nobody asked for this cancellation: the token it carries was never signalled.
        var result = Result.Try(() => throw new OperationCanceledException());

        Assert.Equal(ErrorCategory.Unavailable, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void Try_WhenTheExceptionsTokenDidFire_ReportsCanceled()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        var result = Result.Try(() => source.Token.ThrowIfCancellationRequested());

        Assert.Equal(ErrorCategory.Canceled, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void Try_WithTheCallersCancelledToken_ReportsCanceled()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        var result = Result.Try(token => token.ThrowIfCancellationRequested(), source.Token);

        Assert.Equal(ErrorCategory.Canceled, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void Try_WithACallerTokenThatDidNotFire_ReportsAnInternalCancellationAsUnavailable()
    {
        // The caller's token is live; a linked timeout source inside the operation fired instead.
        using var caller = new CancellationTokenSource();
        using var internalTimeout = new CancellationTokenSource();
        internalTimeout.Cancel();

        var result = Result.Try(
            _ => internalTimeout.Token.ThrowIfCancellationRequested(),
            caller.Token);

        Assert.Equal(ErrorCategory.Unavailable, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void Try_WithACallerToken_CarriesTheValueOnSuccess()
    {
        var result = Result.Try(token => token.CanBeCanceled ? 1 : 2, CancellationToken.None);

        Assert.Equal(2, result.Value);
    }

    [Fact]
    public void Try_WithACallerTokenAndAFunction_ReportsCallerCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        var result = Result.Try<int>(token => throw new OperationCanceledException(token), source.Token);

        Assert.Equal(ErrorCategory.Canceled, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public async Task TryAsync_WithTheCallersCancelledToken_ReportsCanceled()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var result = await Result.TryAsync(token => Task.Delay(10, token), source.Token);

        Assert.Equal(ErrorCategory.Canceled, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public async Task TryAsync_WithACallerTokenThatDidNotFire_ReportsATimeoutAsUnavailable()
    {
        using var caller = new CancellationTokenSource();

        var result = await Result.TryAsync<int>(
            _ => throw new TaskCanceledException("t", new TimeoutException()),
            caller.Token);

        Assert.Equal(ErrorCategory.Unavailable, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public async Task TryAsync_WithACallerTokenAndAFunction_CarriesTheValueOnSuccess()
    {
        var result = await Result.TryAsync(_ => Task.FromResult("v"), CancellationToken.None);

        Assert.Equal("v", result.Value);
    }

    [Fact]
    public async Task ThenAsync_WhenTheOperationTimesOut_ReportsUnavailable()
    {
        var result = await Result<int>.Success(1)
            .ThenAsync<int>(_ => throw new TaskCanceledException("t", new TimeoutException()));

        Assert.Equal(ErrorCategory.Unavailable, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void Canceled_WithAnException_ReportsCancellation()
    {
        var exception = new OperationCanceledException();

        Assert.Equal(ErrorCategory.Canceled, Error.Canceled(exception).Category);
        Assert.Equal(Error.CanceledUserMessage, Error.Canceled(exception).UserMessage);
    }

    [Fact]
    public void Canceled_WithANullException_ReturnsASentinelRatherThanThrowing()
    {
        var error = Error.Canceled(null);

        Assert.Equal(ErrorCategory.Canceled, error.Category);
        Assert.Contains("without any exception detail", error.TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FromCancellation_WithANullException_ReturnsACanceledSentinel()
    {
        Assert.Equal(ErrorCategory.Canceled, Error.FromCancellation(null).Category);
    }

    [Fact]
    public void FromCancellation_WithTheCallersToken_PrefersTheCallersEvidenceOverATimeoutInner()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        var error = Error.FromCancellation(
            new TaskCanceledException("t", new TimeoutException()),
            source.Token);

        Assert.Equal(ErrorCategory.Canceled, error.Category);
    }

    [Fact]
    public void Try_WithAHandler_ClassifiesACancellationBeforeConsultingTheHandler()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        var result = Result.Try(
            () => source.Token.ThrowIfCancellationRequested(),
            ex => Error.FromMessage("handled"));

        Assert.Equal(ErrorCategory.Canceled, Assert.Single(result.Errors).Category);
    }
}
