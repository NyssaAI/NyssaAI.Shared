using Xunit;

namespace NyssaAI.Results.Tests;

public sealed class ResultGenericTests
{
    [Fact]
    public void Success_WithAValue_CreatesASucceededResult()
    {
        var expected = 42;
        var result = Result<int>.Success(expected);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void Failure_WithMessage_CreatesFailureResult()
    {
        var message = "Operation failed";
        var result = Result<int>.Failure(message);

        Assert.False(result.IsSuccess);
        Assert.Single(result.Errors);
        Assert.Equal(message, result.Errors[0].UserMessage);
        Assert.Equal(message, result.Errors[0].TechnicalMessage);
    }

    [Fact]
    public void Failure_WithSeparateMessages_CreatesFailureResult()
    {
        var userMsg = "Something went wrong";
        var techMsg = "Database connection timeout";
        var result = Result<int>.Failure(Error.FromMessages(userMsg, techMsg));

        Assert.False(result.IsSuccess);
        Assert.Single(result.Errors);
        Assert.Equal(userMsg, result.Errors[0].UserMessage);
        Assert.Equal(techMsg, result.Errors[0].TechnicalMessage);
    }

    [Fact]
    public void Failure_WithError_CreatesFailureResult()
    {
        var error = Error.FromMessages("User", "Technical");
        var result = Result<int>.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.Single(result.Errors);
        Assert.Equal(error, result.Errors[0]);
    }

    [Fact]
    public void Failure_WithMultipleErrors_AccumulatesAll()
    {
        var errors = new[] {
            Error.FromMessage("Error 1"),
            Error.FromMessage("Error 2"),
            Error.FromMessage("Error 3")
        };
        var result = Result<int>.Failure(errors);

        Assert.False(result.IsSuccess);
        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public void Then_OnSuccess_ChainsToNextOperation()
    {
        var result = Result<int>.Success(5);

        var final = result.Then(value =>
            Result<int>.Success(value * 2));

        Assert.True(final.IsSuccess);
        Assert.Equal(10, final.Value);
    }

    [Fact]
    public void Then_OnFailure_PropagatesToChain()
    {
        var result = Result<int>.Failure("Initial error");

        var final = result.Then(value =>
            Result<int>.Success(value * 2));

        Assert.False(final.IsSuccess);
        Assert.Equal("Initial error", final.Errors[0].UserMessage);
    }

    [Fact]
    public void Then_WhenOperationThrows_CapturesException()
    {
        var result = Result<int>.Success(5);

        var final = result.Then<int>(_ =>
            throw new InvalidOperationException("Operation failed"));

        Assert.False(final.IsSuccess);
        Assert.Equal("An unexpected error occurred", final.Errors[0].UserMessage);
        Assert.Contains("Operation failed", final.Errors[0].TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Transform_OnSuccess_TransformsValue()
    {
        var result = Result<int>.Success(5);

        var transformed = result.Transform(value => value * 2);

        Assert.True(transformed.IsSuccess);
        Assert.Equal(10, transformed.Value);
    }

    [Fact]
    public void Transform_OnSuccess_CanChangeType()
    {
        var result = Result<int>.Success(42);

        var transformed = result.Transform(value => value.ToString());

        Assert.True(transformed.IsSuccess);
        Assert.Equal("42", transformed.Value);
    }

    [Fact]
    public void Transform_OnFailure_PropagatesErrors()
    {
        var result = Result<int>.Failure("Error");

        var transformed = result.Transform(value => value.ToString());

        Assert.False(transformed.IsSuccess);
        Assert.Equal("Error", transformed.Errors[0].UserMessage);
    }

    [Fact]
    public void Resolve_WithSuccess_CallsSuccessFunction()
    {
        var result = Result<int>.Success(42);

        var resolved = result.Resolve(
            success => $"Success: {success}",
            errors => $"Failed: {errors.Count} errors");

        Assert.Equal("Success: 42", resolved);
    }

    [Fact]
    public void Resolve_WithFailure_CallsFailureFunction()
    {
        var result = Result<int>.Failure(Error.FromMessages("Error 1", "Error 2"));

        var resolved = result.Resolve(
            success => $"Success: {success}",
            errors => $"Failed: {errors.Count} errors");

        Assert.Equal("Failed: 1 errors", resolved);
    }

    [Fact]
    public void Resolve_WithActionsOnASuccess_RunsTheSuccessAction()
    {
        var result = Result<int>.Success(42);
        var called = false;

        result.Resolve(
            success => called = success == 42,
            errors => called = false);

        Assert.True(called);
    }

    [Fact]
    public void Resolve_OnFailure_CallsFailureAction()
    {
        var result = Result<int>.Failure("Error");
        var errorCount = 0;

        result.Resolve(
            success => { },
            errors => errorCount = errors.Count);

        Assert.Equal(1, errorCount);
    }

    [Fact]
    public void Then_WhenAnEarlyStepFails_PropagatesTheErrorsThroughTheChain()
    {
        var result = Result<int>.Success(5)
            .Then(_ => Result<int>.Failure("Error 1"))
            .Then(_ => Result<int>.Failure("Error 2"));

        Assert.False(result.IsSuccess);
        Assert.Single(result.Errors); // First error stops the chain
        Assert.Equal("Error 1", result.Errors[0].UserMessage);
    }

    [Fact]
    public void Transform_FollowedByThen_ChainsBothOperations()
    {
        var result = Result<int>.Success(5)
            .Transform(v => v * 2)
            .Then(v => Result<int>.Success(v + 3));

        Assert.True(result.IsSuccess);
        Assert.Equal(13, result.Value); // (5 * 2) + 3
    }

    [Fact]
    public void Failure_WithAnEmptyList_IsStillAFailure()
    {
        var result = Result<int>.Failure(new List<Error>());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Unexpected, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void Failure_WithAnEmptyArray_IsStillAFailure()
    {
        var result = Result<int>.Failure(Array.Empty<Error>());

        Assert.True(result.IsFailure);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Failure_WithOnlyNullEntries_DropsThemAndStaysAFailure()
    {
        var result = Result<int>.Failure(new Error?[] { null, null });

        Assert.True(result.IsFailure);
        Assert.Single(result.Errors);
        Assert.NotNull(result.Errors[0]);
    }

    [Fact]
    public void Failure_WithANullError_SubstitutesASentinel()
    {
        var result = Result<int>.Failure((Error?)null);

        Assert.True(result.IsFailure);
        Assert.NotNull(Assert.Single(result.Errors));
    }

    [Fact]
    public void Failure_WithAnErrorCollection_AccumulatesAll()
    {
        var result = Result<int>.Failure([
            Error.FromMessage("one"),
            Error.FromMessage("two"),
            Error.FromMessage("three")]);

        Assert.Equal(3, result.Errors.Count);
        Assert.Equal("three", result.Errors[2].UserMessage);
    }

    [Fact]
    public void Failure_WithAnExceptionSequence_AccumulatesOneErrorPerException()
    {
        var result = Result<int>.Failure(Error.FromExceptions(
            new Exception[] { new InvalidOperationException("a"), new TimeoutException("b") },
            "Failed.",
            ErrorCategory.Unavailable));

        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, error => Assert.Equal(ErrorCategory.Unavailable, error.Category));
        Assert.Contains("TimeoutException: b", result.Errors[1].TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Failure_WithAnEmptyExceptionSequence_IsStillAFailure()
    {
        var result = Result<int>.Failure(Error.FromExceptions(Array.Empty<Exception>()));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Value_OnAFailure_IsDefault()
    {
        Assert.Null(Result<string>.Failure("x").Value);
        Assert.Equal(0, Result<int>.Failure("x").Value);
    }

    [Fact]
    public void TryGetValue_OnSuccess_ReturnsTrueAndTheValue()
    {
        var found = Result<string>.Success("v").TryGetValue(out var value);

        Assert.True(found);
        Assert.Equal("v", value);
    }

    [Fact]
    public void TryGetValue_OnFailure_ReturnsFalseAndDefault()
    {
        var found = Result<string>.Failure("x").TryGetValue(out var value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void Errors_OnFailure_ContainsTheFailureDetails()
    {
        var result = Result<int>.Failure("x");
        Assert.True(result.IsFailure);
        Assert.Equal("x", Assert.Single(result.Errors).UserMessage);
    }

    [Fact]
    public void Errors_OnSuccess_IsEmpty()
    {
        var result = Result<int>.Success(1);
        Assert.False(result.IsFailure);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Transform_WhenTheProjectionThrows_CapturesTheException()
    {
        var result = Result<int>.Success(5).Transform<int>(_ => throw new InvalidOperationException("projection"));

        Assert.True(result.IsFailure);
        Assert.Contains("projection", Assert.Single(result.Errors).TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Then_OnFailure_SharesTheErrorListWithoutCopying()
    {
        var failed = Result<int>.Failure("x");

        var chained = failed.Then(value => Result<string>.Success(value.ToString()));

        Assert.Same(failed.Errors, chained.Errors);
    }
}
