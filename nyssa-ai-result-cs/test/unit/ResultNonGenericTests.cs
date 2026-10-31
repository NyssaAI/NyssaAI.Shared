using Xunit;

namespace NyssaAI.Results.Tests;

public sealed class ResultNonGenericTests
{
    [Fact]
    public void Success_WithNoArguments_CreatesASucceededResult()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Failure_WithMessage_CreatesFailureResult()
    {
        var message = "Operation failed";
        var result = Result.Failure(message);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Single(result.Errors);
        Assert.Equal(message, result.Errors[0].UserMessage);
    }

    [Fact]
    public void Failure_WithSeparateMessages_CreatesFailureResult()
    {
        var userMsg = "Something went wrong";
        var techMsg = "Database error";
        var result = Result.Failure(Error.FromMessages(userMsg, techMsg));

        Assert.False(result.IsSuccess);
        Assert.Equal(userMsg, result.Errors[0].UserMessage);
        Assert.Equal(techMsg, result.Errors[0].TechnicalMessage);
    }

    [Fact]
    public void Failure_WithError_CreatesFailureResult()
    {
        var error = Error.FromMessages("User message", "Technical message");
        var result = Result.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.Single(result.Errors);
        Assert.Equal(error, result.Errors[0]);
    }

    [Fact]
    public void Failure_WithMultipleErrors_AccumulatesAll()
    {
        var errors = new[] {
            Error.FromMessage("Error 1"),
            Error.FromMessage("Error 2")
        };
        var result = Result.Failure(errors);

        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Then_OnSuccess_ChainsToNextOperation()
    {
        var result = Result.Success()
            .Then(() => Result.Success());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Then_OnFailure_StopsChain()
    {
        var result = Result.Failure("Error")
            .Then(() => Result.Success());

        Assert.False(result.IsSuccess);
        Assert.Equal("Error", result.Errors[0].UserMessage);
    }

    [Fact]
    public void Then_ToGenericResult_ChainsOperation()
    {
        var result = Result.Success()
            .Then(() => Result<int>.Success(42));

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Try_WithAction_WrapsExceptions()
    {
        var result = Result.Try(() =>
        {
            throw new InvalidOperationException("Operation failed");
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("An unexpected error occurred", result.Errors[0].UserMessage);
        Assert.Contains("Operation failed", result.Errors[0].TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Try_WithAnActionThatDoesNotThrow_Succeeds()
    {
        var called = false;
        var result = Result.Try(() => called = true);

        Assert.True(result.IsSuccess);
        Assert.True(called);
    }

    [Fact]
    public void Try_WithFunc_WrapsExceptions()
    {
        var result = Result.Try<int>(() =>
            throw new InvalidOperationException("Failed"));

        Assert.False(result.IsSuccess);
        Assert.Contains("Failed", result.Errors[0].TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Try_WithFunc_ReturnsValue()
    {
        var result = Result.Try(() => 42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Try_WithCustomErrorHandler_UsesHandler()
    {
        var result = Result.Try(
            () => throw new InvalidOperationException("Custom error"),
            ex => Error.FromMessage($"Handled: {ex.Message}"));

        Assert.False(result.IsSuccess);
        Assert.Equal("Handled: Custom error", result.Errors[0].UserMessage);
    }

    [Fact]
    public void Resolve_OnSuccess_CallsSuccessAction()
    {
        var result = Result.Success();
        var called = false;

        result.Resolve(
            () => called = true,
            _ => called = false);

        Assert.True(called);
    }

    [Fact]
    public void Resolve_OnFailure_CallsFailureAction()
    {
        var result = Result.Failure("Error");
        var errorCount = 0;

        result.Resolve(
            () => { },
            errors => errorCount = errors.Count);

        Assert.Equal(1, errorCount);
    }

    [Fact]
    public void Resolve_WithFunctions_ReturnsTheValueFromTheMatchingBranch()
    {
        var successResult = Result.Success();
        var failureResult = Result.Failure("Error");

        var successResolved = successResult.Resolve(
            () => "Success",
            _ => "Failure");

        var failureResolved = failureResult.Resolve(
            () => "Success",
            _ => "Failure");

        Assert.Equal("Success", successResolved);
        Assert.Equal("Failure", failureResolved);
    }

    [Fact]
    public void ToGeneric_OnSuccess_CreatesGenericSuccess()
    {
        var result = Result.Success()
            .ToGeneric(() => 42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ToGeneric_OnFailure_PropagatesToGeneric()
    {
        var result = Result.Failure("Error")
            .ToGeneric(() => 42);

        Assert.False(result.IsSuccess);
        Assert.Equal("Error", result.Errors[0].UserMessage);
    }

    [Fact]
    public void ToGeneric_WhenProviderThrows_CatchesException()
    {
        var result = Result.Success()
            .ToGeneric<int>(() =>
                throw new InvalidOperationException("Provider failed"));

        Assert.False(result.IsSuccess);
        Assert.Contains("Provider failed", result.Errors[0].TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Failure_WithAnEmptyList_IsStillAFailure()
    {
        var result = Result.Failure(new List<Error>());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Unexpected, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void Failure_WithAnEmptyArray_IsStillAFailure()
    {
        Assert.True(Result.Failure(Array.Empty<Error>()).IsFailure);
    }

    [Fact]
    public void Failure_WithANullError_SubstitutesASentinel()
    {
        var result = Result.Failure((Error?)null);

        Assert.True(result.IsFailure);
        Assert.NotNull(Assert.Single(result.Errors));
    }

    [Fact]
    public void Failure_WithAnErrorCollection_AccumulatesAll()
    {
        var result = Result.Failure([Error.FromMessage("one"), Error.FromMessage("two")]);

        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Failure_WithAListOfErrors_BindsToTheEnumerableOverload()
    {
        var list = new List<Error> { Error.FromMessage("one") };

        Assert.Equal("one", Assert.Single(Result.Failure(list).Errors).UserMessage);
    }

    [Fact]
    public void Errors_OnAFailureAndOnASuccess_ReportsEachState()
    {
        var failure = Result.Failure("x");
        Assert.True(failure.IsFailure);
        var errors = failure.Errors;
        Assert.Single(errors);

        var success = Result.Success();
        Assert.False(success.IsFailure);
        var none = success.Errors;
        Assert.Empty(none);
    }

    [Fact]
    public void Then_WhenTheOperationThrows_CapturesTheException()
    {
        var result = Result.Success().Then(() => throw new InvalidOperationException("next"));

        Assert.True(result.IsFailure);
        Assert.Contains("next", Assert.Single(result.Errors).TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Then_OnSuccess_ReturnsTheNextResultAsIs()
    {
        var next = Result.Failure("next");

        Assert.Same(next, Result.Success().Then(() => next));
    }

    [Fact]
    public void Try_WithFuncAndHandler_UsesTheHandler()
    {
        var result = Result.Try<int>(
            () => throw new InvalidOperationException("Custom"),
            ex => Error.FromMessage($"Handled: {ex.Message}", ErrorCategory.Conflict));

        Assert.True(result.IsFailure);
        Assert.Equal("Handled: Custom", result.Errors[0].UserMessage);
        Assert.Equal(ErrorCategory.Conflict, result.Errors[0].Category);
    }

    [Fact]
    public void Try_WithAHandlerThatReturnsNull_StillReportsAFailure()
    {
        // The handler's null return is deliberate; it must be normalized to the sentinel error.
        var result = Result.Try(() => throw new InvalidOperationException("x"), _ => null!);

        Assert.True(result.IsFailure);
        Assert.NotNull(Assert.Single(result.Errors));
    }

    [Fact]
    public async Task ResolveAsync_WithFunctions_ChoosesTheMatchingBranch()
    {
        var success = await Result.Success().ResolveAsync(
            () => Task.FromResult("Success"),
            _ => Task.FromResult("Failure"));
        var failure = await Result.Failure("x").ResolveAsync(
            () => Task.FromResult("Success"),
            errors => Task.FromResult($"Failure:{errors.Count}"));

        Assert.Equal("Success", success);
        Assert.Equal("Failure:1", failure);
    }

    [Fact]
    public async Task ResolveAsync_WithActions_RunsOnlyTheMatchingBranch()
    {
        var log = new List<string>();

        await Result.Success().ResolveAsync(
            () => { log.Add("success"); return Task.CompletedTask; },
            _ => { log.Add("failure"); return Task.CompletedTask; });
        await Result.Failure("x").ResolveAsync(
            () => { log.Add("success"); return Task.CompletedTask; },
            _ => { log.Add("failure"); return Task.CompletedTask; });

        Assert.Equal(new[] { "success", "failure" }, log);
    }
}
