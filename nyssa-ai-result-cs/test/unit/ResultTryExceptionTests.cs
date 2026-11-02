using System.Runtime.CompilerServices;
using Xunit;

namespace NyssaAI.Results.Tests;

public sealed class ResultTryExceptionTests
{
    [Fact]
    public void GenericFailure_FromException_CarriesBothMessagesAndTheStackTrace()
    {
        var caught = Caught(ThrowTimeout);

        var result = Result<int>.Failure(Error.FromExceptions(caught, "Could not read the file."));

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Could not read the file.", error.UserMessage);
        Assert.Equal("TimeoutException: the well ran dry", error.TechnicalMessage);
        Assert.Contains(nameof(ThrowTimeout), error.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericFailure_FromAggregateException_AccumulatesOneErrorPerFailure()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("first"),
            new TimeoutException("second"));

        var result = Result<int>.Failure(Error.FromExceptions(aggregate));

        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void NonGenericFailure_FromExceptionSequence_AccumulatesOneErrorPerException()
    {
        var exceptions = new Exception[]
        {
            new InvalidOperationException("first"),
            new TimeoutException("second")
        };

        var result = Result.Failure(Error.FromExceptions(exceptions, "The batch failed."));

        Assert.True(result.IsFailure);
        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, error => Assert.Equal("The batch failed.", error.UserMessage));
    }

    [Fact]
    public void NonGenericFailure_FromEmptyExceptionSequence_DoesNotReportSuccess()
    {
        var result = Result.Failure(Error.FromExceptions(Array.Empty<Exception>()));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Try_WhenTheActionThrows_CapturesTheException()
    {
        var result = Result.Try(ThrowTimeout);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(Error.DefaultUserMessage, error.UserMessage);
        Assert.Equal("TimeoutException: the well ran dry", error.TechnicalMessage);
        Assert.Contains(nameof(ThrowTimeout), error.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryAsync_WhenTheFunctionThrows_CapturesTheException()
    {
        var result = await Result.TryAsync<int>(
            () => throw new TimeoutException("the well ran dry"));

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal("TimeoutException: the well ran dry", error.TechnicalMessage);
        Assert.NotNull(error.StackTrace);
    }

    [Fact]
    public void Try_WhenDerivedResultExceptionHasNoErrors_ReturnsFailure()
    {
        var exception = new EmptyResultException();

        var result = Result.Try(() => throw exception);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(Error.DefaultUserMessage, error.UserMessage);
        Assert.Equal(error.UserMessage, exception.Message);
    }

    [Fact]
    public void TryGeneric_WhenDerivedResultExceptionHasNoErrors_ReturnsFailure()
    {
        var exception = new EmptyResultException();

        var result = Result.Try<int>(() => throw exception);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(Error.DefaultUserMessage, error.UserMessage);
        Assert.Equal(error.UserMessage, exception.Message);
    }

    [Fact]
    public void Try_WithActionAndErrorHandlerAndToken_OnSuccess_ReturnsSuccess()
    {
        var executed = false;
        var result = Result.Try(
            token => { executed = true; },
            ex => Error.FromMessage(ex.Message),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(executed);
    }

    [Fact]
    public void Try_WithActionAndErrorHandlerAndToken_WhenThrows_UsesErrorHandler()
    {
        var result = Result.Try(
            _ => throw new InvalidOperationException("disk locked"),
            ex => Error.FromMessage($"Mapped: {ex.Message}", ErrorCategory.Conflict),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Mapped: disk locked", error.UserMessage);
        Assert.Equal(ErrorCategory.Conflict, error.Category);
    }

    [Fact]
    public void Try_WithActionAndErrorHandlerAndToken_WhenCanceledByCaller_ReportsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = Result.Try(
            token => throw new OperationCanceledException(token),
            _ => Error.FromMessage("should not reach here"),
            cts.Token);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Canceled, error.Category);
    }

    [Fact]
    public void TryGeneric_WithFuncAndErrorHandlerAndToken_OnSuccess_ReturnsValue()
    {
        var result = Result.Try(
            _ => 99,
            ex => Error.FromMessage(ex.Message),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(99, result.Value);
    }

    [Fact]
    public void TryGeneric_WithFuncAndErrorHandlerAndToken_WhenThrows_UsesErrorHandler()
    {
        var result = Result.Try<int>(
            _ => throw new FormatException("bad number"),
            ex => Error.FromMessage($"Parse error: {ex.Message}", ErrorCategory.Validation),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Parse error: bad number", error.UserMessage);
        Assert.Equal(ErrorCategory.Validation, error.Category);
    }

    [Fact]
    public void TryGeneric_WithFuncAndErrorHandlerAndToken_WhenTimesOut_ReportsUnavailable()
    {
        using var cts = new CancellationTokenSource();

        var result = Result.Try<int>(
            _ => throw new TaskCanceledException("t", new TimeoutException()),
            _ => Error.FromMessage("should not reach here"),
            cts.Token);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Unavailable, error.Category);
    }

    [Fact]
    public async Task TryAsync_WithActionAndErrorHandlerAndToken_OnSuccess_ReturnsSuccess()
    {
        var ran = false;
        var result = await Result.TryAsync(
            _ => { ran = true; return Task.CompletedTask; },
            ex => Error.FromMessage(ex.Message),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(ran);
    }

    [Fact]
    public async Task TryAsync_WithActionAndErrorHandlerAndToken_WhenThrows_UsesErrorHandler()
    {
        var result = await Result.TryAsync(
            _ => Task.FromException(new InvalidOperationException("network down")),
            ex => Error.FromMessage(ex.Message, ErrorCategory.Unavailable),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Unavailable, error.Category);
        Assert.Equal("network down", error.UserMessage);
    }

    [Fact]
    public async Task TryAsync_WithActionAndErrorHandlerAndToken_WhenCanceledByCaller_ReportsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await Result.TryAsync(
            token => Task.FromException(new OperationCanceledException(token)),
            _ => Error.FromMessage("should not be called"),
            cts.Token);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Canceled, error.Category);
    }

    [Fact]
    public async Task TryAsyncGeneric_WithFuncAndErrorHandlerAndToken_OnSuccess_ReturnsValue()
    {
        var result = await Result.TryAsync(
            _ => Task.FromResult("payload"),
            ex => Error.FromMessage(ex.Message),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("payload", result.Value);
    }

    [Fact]
    public async Task TryAsyncGeneric_WithFuncAndErrorHandlerAndToken_WhenThrows_UsesErrorHandler()
    {
        var result = await Result.TryAsync<string>(
            _ => Task.FromException<string>(new InvalidOperationException("duplicate entry")),
            ex => Error.FromMessage(ex.Message, ErrorCategory.Conflict),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Conflict, error.Category);
        Assert.Equal("duplicate entry", error.UserMessage);
    }

    [Fact]
    public async Task TryAsyncGeneric_WithFuncAndErrorHandlerAndToken_WhenCanceledByCaller_ReportsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await Result.TryAsync<string>(
            token => Task.FromException<string>(new OperationCanceledException(token)),
            _ => Error.FromMessage("should not be called"),
            cts.Token);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Canceled, error.Category);
    }

    private sealed class EmptyResultException() : ResultException(Array.Empty<Error>());

    private static Exception Caught(Action throwing)
    {
        try
        {
            throwing();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("The throw-site helper did not throw.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowTimeout() => throw new TimeoutException("the well ran dry");
}
