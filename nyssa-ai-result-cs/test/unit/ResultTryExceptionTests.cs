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
