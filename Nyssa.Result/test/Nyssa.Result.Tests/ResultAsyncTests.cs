using Xunit;

namespace NyssaAI.Results.Tests;

public sealed class ResultAsyncTests
{
    [Fact]
    public async Task ThenAsync_OnSuccess_RunsTheNextOperation()
    {
        var result = await Result<int>.Success(2)
            .ThenAsync(value => Task.FromResult(Result<string>.Success($"got {value}")));

        Assert.True(result.IsSuccess);
        Assert.Equal("got 2", result.Value);
    }

    [Fact]
    public async Task ThenAsync_OnFailure_SkipsTheNextOperationAndKeepsTheErrors()
    {
        var ran = false;
        var failure = Error.FromMessage("no", ErrorCategory.Validation);

        var result = await Result<int>.Failure(failure)
            .ThenAsync(_ =>
            {
                ran = true;
                return Task.FromResult(Result<string>.Success("unreachable"));
            });

        Assert.False(ran);
        Assert.True(result.IsFailure);
        Assert.Equal(failure, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task ThenAsync_WhenTheOperationThrows_ReturnsAFailure()
    {
        var result = await Result<int>.Success(1)
            .ThenAsync<string>(_ => throw new InvalidOperationException("boom"));

        Assert.True(result.IsFailure);
        Assert.Contains("boom", Assert.Single(result.Errors).TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThenAsync_WhenTheOperationIsCancelled_ReportsCanceled()
    {
        var result = await Result<int>.Success(1)
            .ThenAsync<string>(_ => throw new OperationCanceledException(new CancellationToken(canceled: true)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Canceled, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public async Task TransformAsync_OnSuccess_MapsTheValue()
    {
        var result = await Result<int>.Success(21).TransformAsync(value => Task.FromResult(value * 2));

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task TransformAsync_OnFailure_PropagatesTheErrors()
    {
        var failure = Error.FromMessage("no", ErrorCategory.NotFound);

        var result = await Result<int>.Failure(failure).TransformAsync(value => Task.FromResult(value * 2));

        Assert.True(result.IsFailure);
        Assert.Equal(failure, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task TransformAsync_WhenTheProjectionThrows_CapturesTheException()
    {
        var result = await Result<int>.Success(5)
            .TransformAsync<int>(_ => throw new InvalidOperationException("projection"));

        Assert.True(result.IsFailure);
        Assert.Contains("projection", Assert.Single(result.Errors).TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_OnEitherState_EliminatesTheResult()
    {
        var fromSuccess = await Result<int>.Success(7)
            .ResolveAsync(value => Task.FromResult($"ok {value}"), errors => Task.FromResult($"failed {errors.Count}"));

        var fromFailure = await Result<int>.Failure("bad")
            .ResolveAsync(value => Task.FromResult($"ok {value}"), errors => Task.FromResult($"failed {errors.Count}"));

        Assert.Equal("ok 7", fromSuccess);
        Assert.Equal("failed 1", fromFailure);
    }

    [Fact]
    public async Task ResolveAsync_OnSuccess_RunsOnlyTheSuccessBranch()
    {
        var successRan = false;
        var failureRan = false;

        await Result<int>.Success(1).ResolveAsync(
            _ => { successRan = true; return Task.CompletedTask; },
            _ => { failureRan = true; return Task.CompletedTask; });

        Assert.True(successRan);
        Assert.False(failureRan);
    }

    [Fact]
    public async Task ResolveAsync_OnFailure_RunsOnlyTheFailureBranch()
    {
        var successRan = false;
        var failureRan = false;

        await Result<int>.Failure("bad").ResolveAsync(
            _ => { successRan = true; return Task.CompletedTask; },
            _ => { failureRan = true; return Task.CompletedTask; });

        Assert.False(successRan);
        Assert.True(failureRan);
    }

    [Fact]
    public async Task TryAsync_TranslatesAThrownExceptionIntoAFailure()
    {
        var result = await Result.TryAsync(() => throw new InvalidOperationException("boom"));

        Assert.True(result.IsFailure);
        Assert.Contains("boom", Assert.Single(result.Errors).TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryAsync_OnSuccess_CarriesTheValue()
    {
        var result = await Result.TryAsync(() => Task.FromResult(42));

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task TryAsync_WithAnErrorHandler_ReturnsTheHandlersError()
    {
        var mapped = Error.FromMessage("mapped", ErrorCategory.Conflict);

        var result = await Result.TryAsync(
            () => throw new InvalidOperationException("boom"),
            _ => mapped);

        Assert.True(result.IsFailure);
        Assert.Equal(mapped, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task TryAsync_WhenCancelled_ReportsCanceledRatherThanUnavailable()
    {
        // Cancellation and a transient fault call for opposite responses: one invites a retry, the
        // other was asked for. Before ErrorCategory.Canceled existed, every stage reported both the
        // same way.
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await Result.TryAsync(async () =>
        {
            await Task.Yield();
            cancellation.Token.ThrowIfCancellationRequested();
        });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Canceled, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public async Task ThenAsync_ChainsOnTheNonGenericResult()
    {
        var result = await Result.Success().ThenAsync(() => Task.FromResult(Result<int>.Success(5)));

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value);
    }

    [Fact]
    public async Task ThenAsync_OnAFailedNonGenericResult_SkipsTheContinuation()
    {
        var ran = false;

        var result = await Result.Failure("no").ThenAsync(() =>
        {
            ran = true;
            return Task.FromResult(Result.Success());
        });

        Assert.False(ran);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ThenAsync_OnASucceededNonGenericResult_RunsTheContinuation()
    {
        var ran = false;

        var result = await Result.Success().ThenAsync(() =>
        {
            ran = true;
            return Task.FromResult(Result.Success());
        });

        Assert.True(ran);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ThenAsync_WhenTheNonGenericContinuationThrows_ReturnsAFailure()
    {
        // Held in a typed local so overload resolution lands on ThenAsync(Func<Task<Result>>): a
        // lambda whose body only throws offers nothing to infer TNext from, so written inline it
        // would pick that overload by omission rather than by intent.
        Func<Task<Result>> throwing = () => throw new InvalidOperationException("boom");

        var result = await Result.Success().ThenAsync(throwing);

        Assert.True(result.IsFailure);
        Assert.Contains("boom", Assert.Single(result.Errors).TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThenAsync_OnAFailedNonGenericResult_SkipsTheValueContinuationAndKeepsTheErrors()
    {
        var ran = false;
        var failure = Error.FromMessage("no", ErrorCategory.Validation);

        var result = await Result.Failure(failure).ThenAsync(() =>
        {
            ran = true;
            return Task.FromResult(Result<int>.Success(5));
        });

        Assert.False(ran);
        Assert.True(result.IsFailure);
        Assert.Equal(failure, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task ThenAsync_WhenTheValueContinuationThrows_ReturnsAFailure()
    {
        var result = await Result.Success()
            .ThenAsync<int>(() => throw new InvalidOperationException("boom"));

        Assert.True(result.IsFailure);
        Assert.Contains("boom", Assert.Single(result.Errors).TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryAsync_WithAnActionAndHandler_UsesTheHandler()
    {
        var result = await Result.TryAsync(
            () => throw new InvalidOperationException("act"),
            ex => Error.FromMessage($"Handled: {ex.Message}"));

        Assert.True(result.IsFailure);
        Assert.Equal("Handled: act", Assert.Single(result.Errors).UserMessage);
    }

    [Fact]
    public async Task TryAsync_WithAFunctionAndHandler_UsesTheHandler()
    {
        var result = await Result.TryAsync<int>(
            () => throw new InvalidOperationException("fn"),
            ex => Error.FromMessage($"Handled: {ex.Message}"));

        Assert.True(result.IsFailure);
        Assert.Equal("Handled: fn", Assert.Single(result.Errors).UserMessage);
    }

    [Fact]
    public async Task TryAsync_WithAFunctionAndHandler_CarriesTheValueOnSuccess()
    {
        var result = await Result.TryAsync(() => Task.FromResult(7), ex => Error.FromMessage(ex.Message));

        Assert.Equal(7, result.Value);
    }
}
