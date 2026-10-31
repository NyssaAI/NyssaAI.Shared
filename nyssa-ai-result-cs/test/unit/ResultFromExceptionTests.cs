using System.Runtime.CompilerServices;
using Xunit;

namespace NyssaAI.Results.Tests;

public sealed class ResultFromExceptionTests
{
    [Fact]
    public void FromException_WithNoUserMessage_UsesTheDefaultUserMessage()
    {
        var error = Error.FromException(new InvalidOperationException("boom"));

        Assert.Equal(Error.DefaultUserMessage, error.UserMessage);
    }

    [Fact]
    public void FromException_WithACallerSuppliedUserMessage_KeepsIt()
    {
        var error = Error.FromException(new InvalidOperationException("boom"), "Something went wrong.");

        Assert.Equal("Something went wrong.", error.UserMessage);
    }

    [Fact]
    public void FromException_WithASimpleException_DerivesTechnicalMessageFromTypeAndMessage()
    {
        var error = Error.FromException(new InvalidOperationException("boom"));

        Assert.Equal("InvalidOperationException: boom", error.TechnicalMessage);
    }

    [Fact]
    public void FromException_WithAnInnerChain_DerivesTechnicalMessageFromEveryLevel()
    {
        var exception = new InvalidOperationException(
            "outer",
            new TimeoutException("inner", new ArgumentException("root")));

        var error = Error.FromException(exception);

        Assert.Equal(
            "InvalidOperationException: outer ---> TimeoutException: inner ---> ArgumentException: root",
            error.TechnicalMessage);
    }

    [Fact]
    public void FromException_WithAThrownException_CapturesTheStackTrace()
    {
        var caught = Caught(ThrowTimeout);

        var error = Error.FromException(caught, "The lookup timed out.");

        Assert.Equal(caught.StackTrace, error.StackTrace);
        Assert.Contains(nameof(ThrowTimeout), error.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void FromException_WithAThrownInnerException_ComposesOuterThenInnerFramesExactly()
    {
        var inner = Caught(ThrowIO);
        var outer = Caught(() => throw new InvalidOperationException("could not index", inner));

        var error = Error.FromException(outer, "Indexing failed.");

        var expected = outer.StackTrace
            + Environment.NewLine
            + "--- inner exception: IOException ---"
            + Environment.NewLine
            + inner.StackTrace;

        Assert.Equal(expected, error.StackTrace);
    }

    [Fact]
    public void FromException_WithADeepChain_CapturesEveryLevelOutermostFirst()
    {
        var root = Caught(ThrowIO);
        var middle = Caught(() => throw new TimeoutException("middle", root));
        var outer = Caught(() => throw new InvalidOperationException("outer", middle));

        var error = Error.FromException(outer);

        Assert.Contains(nameof(ThrowIO), error.StackTrace, StringComparison.Ordinal);

        Assert.NotNull(error.StackTrace);
        var timeoutMarker = error.StackTrace.IndexOf(
            "--- inner exception: TimeoutException ---", StringComparison.Ordinal);
        var ioMarker = error.StackTrace.IndexOf(
            "--- inner exception: IOException ---", StringComparison.Ordinal);

        Assert.True(timeoutMarker >= 0, "the middle exception's frames are missing");
        Assert.True(ioMarker >= 0, "the root exception's frames are missing");
        Assert.True(timeoutMarker < ioMarker, "frames must run outermost to innermost");
        Assert.NotNull(outer.StackTrace);
        Assert.StartsWith(outer.StackTrace, error.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void FromException_WithAnInnerExceptionNeverThrown_SkipsItWithoutASeparator()
    {
        var outer = Caught(() =>
            throw new InvalidOperationException("could not index", new IOException("never thrown")));

        var error = Error.FromException(outer);

        Assert.Equal(outer.StackTrace, error.StackTrace);
        Assert.DoesNotContain("--- inner exception:", error.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void FromException_WhenTheExceptionWasNeverThrown_LeavesStackTraceNull()
    {
        var error = Error.FromException(new InvalidOperationException("boom"));

        Assert.Null(error.StackTrace);
    }

    [Fact]
    public void FromException_WithNullException_ReturnsASentinelErrorRatherThanThrowing()
    {
        // The public factories never throw: a null argument is normalized, here into an error that
        // says no exception detail was supplied, so the enclosing result still reports a failure.
        var error = Error.FromException(null, "Saving failed.", ErrorCategory.Unavailable);

        Assert.Equal("Saving failed.", error.UserMessage);
        Assert.Contains("without any exception detail", error.TechnicalMessage, StringComparison.Ordinal);
        Assert.Equal(ErrorCategory.Unavailable, error.Category);
        Assert.Null(error.StackTrace);
    }

    [Fact]
    public void FromExceptions_WithANullException_ReturnsOneSentinelError()
    {
        Assert.Single(Error.FromExceptions((Exception?)null));
    }

    [Fact]
    public void FromExceptions_WithANullSequence_ReturnsOneSentinelError()
    {
        Assert.Single(Error.FromExceptions((IEnumerable<Exception>?)null));
    }

    [Fact]
    public void FromExceptions_WithOrdinaryException_ProducesOneError()
    {
        var errors = Error.FromExceptions(new InvalidOperationException("boom"));

        var error = Assert.Single(errors);
        Assert.Equal("InvalidOperationException: boom", error.TechnicalMessage);
    }

    [Fact]
    public void FromExceptions_WithAnAggregateException_FlattensItIntoOneErrorPerFailure()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("first"),
            new TimeoutException("second"));

        var errors = Error.FromExceptions(aggregate, "Several steps failed.");

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal("Several steps failed.", error.UserMessage));
        Assert.Contains(errors, error => error.TechnicalMessage.Contains("first", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.TechnicalMessage.Contains("second", StringComparison.Ordinal));
    }

    [Fact]
    public void FromExceptions_WithNestedAggregateExceptions_FlattensEveryLevel()
    {
        var aggregate = new AggregateException(
            new AggregateException(new InvalidOperationException("inner")),
            new TimeoutException("outer"));

        var errors = Error.FromExceptions(aggregate);

        Assert.Equal(2, errors.Count);
        Assert.DoesNotContain(
            errors,
            error => error.TechnicalMessage.StartsWith("AggregateException", StringComparison.Ordinal));
    }

    [Fact]
    public void FromExceptions_WithAnAggregateException_PairsEachStackTraceWithItsOwnError()
    {
        var timeout = Caught(ThrowTimeout);
        var io = Caught(ThrowIO);
        var aggregate = new AggregateException("Two files could not be indexed.", timeout, io);

        var errors = Error.FromExceptions(aggregate, "Indexing failed.");

        Assert.Equal(2, errors.Count);
        Assert.Equal(timeout.StackTrace, ErrorFor(errors, "TimeoutException").StackTrace);
        Assert.Equal(io.StackTrace, ErrorFor(errors, "IOException").StackTrace);
    }

    [Fact]
    public void FromExceptions_OnAThrownAggregate_KeepsEachInnerTraceNotTheAggregatesOwn()
    {
        var timeout = Caught(ThrowTimeout);
        var io = Caught(ThrowIO);
        var aggregate = Caught(() => throw new AggregateException(timeout, io));

        Assert.NotNull(aggregate.StackTrace);

        var errors = Error.FromExceptions(aggregate, "Indexing failed.");

        Assert.Equal(2, errors.Count);
        Assert.Equal(timeout.StackTrace, ErrorFor(errors, "TimeoutException").StackTrace);
        Assert.Equal(io.StackTrace, ErrorFor(errors, "IOException").StackTrace);
    }

    [Fact]
    public void FromExceptions_KeepsEachInnerTrace_ThroughNestedAggregates()
    {
        var timeout = Caught(ThrowTimeout);
        var io = Caught(ThrowIO);
        var aggregate = new AggregateException(new AggregateException(timeout), io);

        var errors = Error.FromExceptions(aggregate, "Indexing failed.");

        Assert.Equal(2, errors.Count);
        Assert.Equal(timeout.StackTrace, ErrorFor(errors, "TimeoutException").StackTrace);
        Assert.Equal(io.StackTrace, ErrorFor(errors, "IOException").StackTrace);
    }

    [Fact]
    public async Task FromExceptions_WithParallelTaskFailures_CapturesEachTrace()
    {
        var token = TestContext.Current.CancellationToken;
        var task = Task.WhenAll(Task.Run(ThrowTimeout, token), Task.Run(ThrowIO, token));
        await Assert.ThrowsAnyAsync<Exception>(() => task);

        Assert.NotNull(task.Exception);

        var errors = Error.FromExceptions(task.Exception, "Indexing failed.");

        Assert.Equal(2, errors.Count);
        Assert.Contains(
            nameof(ThrowTimeout),
            ErrorFor(errors, "TimeoutException").StackTrace,
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(ThrowIO),
            ErrorFor(errors, "IOException").StackTrace,
            StringComparison.Ordinal);
    }

    private static Error ErrorFor(IReadOnlyList<Error> errors, string exceptionTypeName) =>
        Assert.Single(
            errors,
            error => error.TechnicalMessage.StartsWith(exceptionTypeName, StringComparison.Ordinal));

    [Fact]
    public void FromExceptions_WithEmptyAggregate_StillProducesOneError()
    {
        var errors = Error.FromExceptions(new AggregateException());

        Assert.Single(errors);
    }

    [Fact]
    public void FromExceptions_WithEmptySequence_StillProducesOneError()
    {
        var errors = Error.FromExceptions(Array.Empty<Exception>());

        Assert.Single(errors);
    }

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
    private static void ThrowIO() => throw new IOException("disk gone");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowTimeout() => throw new TimeoutException("the well ran dry");
}
