using Xunit;

namespace NyssaAI.Results.Tests;

public sealed class GuardAgainstTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BooleanGuards_Condition_RejectNamedState(bool condition)
    {
        var againstTrue = Guard.Against.True(condition, "Cannot continue", "Duplicate", ErrorCategory.Conflict);
        var againstFalse = Guard.Against.False(condition, "Cannot continue");
        Assert.Equal(condition, againstTrue.IsFailure);
        Assert.Equal(!condition, againstFalse.IsFailure);
        if (condition)
        {
            var error = Assert.Single(againstTrue.Errors);
            Assert.Equal(ErrorCategory.Conflict, error.Category);
            Assert.Equal("Cannot continue", error.UserMessage);
            Assert.Equal("Duplicate", error.TechnicalMessage);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(42)]
    public void Null_NullableValue_RejectsOnlyNull(int? value)
    {
        var result = Guard.Against.Null(value);
        Assert.Equal(value.HasValue, result.IsSuccess);
        if (value.HasValue) Assert.Equal(value.Value, result.ThrowIfError());
        else Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
    }

    [Theory]
    [InlineData(4, false, false)]
    [InlineData(5, false, true)]
    [InlineData(6, true, true)]
    public void UpperBounds_AtBoundary_RejectNamedComparison(int value, bool greater, bool greaterOrEqual)
    {
        var exclusive = Guard.Against.GreaterThan(value, 5);
        var inclusive = Guard.Against.GreaterThanOrEqual(value, 5);
        Assert.Equal(greater, exclusive.IsFailure);
        Assert.Equal(greaterOrEqual, inclusive.IsFailure);
        if (exclusive.IsSuccess) Assert.Equal(value, exclusive.Value);
        if (inclusive.IsSuccess) Assert.Equal(value, inclusive.Value);
    }

    [Fact]
    public void UpperBounds_NullReference_ReturnValidationFailure()
    {
        // Deliberately violate the non-null input contract to verify defensive validation.
        Assert.Equal(ErrorCategory.Validation, Assert.Single(Guard.Against.GreaterThan<string>(null!, "z").Errors).Category);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(Guard.Against.GreaterThanOrEqual<string>(null!, "z").Errors).Category);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void True_Predicate_RejectsNamedStateAndPreservesAcceptedValue(bool invalid)
    {
        var value = new object();
        var calls = 0;
        var result = Guard.Against.True(value, candidate =>
        {
            calls++;
            Assert.Same(value, candidate);
            return invalid;
        }, "Cannot continue", "Duplicate", ErrorCategory.Conflict);

        Assert.Equal(1, calls);
        Assert.Equal(invalid, result.IsFailure);
        if (result.IsSuccess) Assert.Same(value, result.Value);
        else
        {
            var error = Assert.Single(result.Errors);
            Assert.Equal(ErrorCategory.Conflict, error.Category);
            Assert.Equal("Cannot continue", error.UserMessage);
            Assert.Equal("Duplicate", error.TechnicalMessage);
        }
    }

    [Fact]
    public void True_ThrowingPredicate_ReturnsFailure()
    {
        var result = Guard.Against.True(42, _ => throw new InvalidOperationException("predicate failed"), "Invalid");
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Unexpected, error.Category);
        Assert.Contains("predicate failed", error.TechnicalMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, 1, 3, false)]
    [InlineData("", 0, 0, true)]
    [InlineData("", 1, 3, false)]
    [InlineData("a", 1, 3, true)]
    [InlineData("abc", 1, 3, true)]
    [InlineData("abcd", 1, 3, false)]
    [InlineData("a", -1, 3, false)]
    [InlineData("a", 3, 1, false)]
    public void LengthOutOfRange_Bounds_ReturnExpectedOutcome(string? value, int min, int max, bool succeeds)
    {
        var result = Guard.Against.LengthOutOfRange(value, min, max);
        Assert.Equal(succeeds, result.IsSuccess);
        if (succeeds) Assert.Equal(value, result.Value);
        else Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void Throw_Success_ReturnsTypedValueOrCompletes()
    {
        Result.Success().Throw();
        var value = new object();
        Assert.Same(value, Guard.Against.Null(value).ThrowIfError());
        Assert.Null(Result<string?>.Success(null).ThrowIfError());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Throw_Failure_PreservesAllErrorsAndStopsExecution(bool generic)
    {
        var errors = new[]
        {
            Error.FromMessages("Invalid input", "sentinel-detail", ErrorCategory.Validation),
            Error.FromMessages("Already exists", "duplicate", ErrorCategory.Conflict)
        };
        var continued = false;
        var exception = Assert.Throws<ResultException>(() =>
        {
            if (generic) Result<int>.Failure(errors).ThrowIfError();
            else Result.Failure(errors).ThrowIfError();
            continued = true;
        });
        Assert.False(continued);
        Assert.Equal(errors, exception.Errors);
        Assert.DoesNotContain("sentinel-detail", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.StackTrace);
        Assert.Equal(errors, Result.Failure(Error.FromExceptions(exception)).Errors);
        Assert.Equal(errors, Result.Try(() => throw exception).Errors);
        Assert.Equal(errors, Result.Try<int>(() => throw exception).Errors);
        Assert.Equal(errors, Result.Failure(Error.FromExceptions(new AggregateException(exception))).Errors);
    }

    [Fact]
    public async Task Throw_InsideAsyncBoundary_PreservesErrors()
    {
        var failure = Guard.Against.True(true, "Conflict", "duplicate", ErrorCategory.Conflict);
        var result = await Result.TryAsync(async () =>
        {
            await Task.Yield();
            failure.Throw();
        });
        Assert.Equal(failure.Errors, result.Errors);
    }

    [Fact]
    public void Throw_MixedNestedAggregate_PreservesStructuredErrorsAndMapsOrdinaryException()
    {
        var errors = new[]
        {
            Error.FromMessages("Invalid", "input", ErrorCategory.Validation),
            Error.FromMessages("Conflict", "duplicate", ErrorCategory.Conflict)
        };
        var exception = Assert.Throws<ResultException>(() => Result.Failure(errors).Throw());
        var aggregate = new AggregateException(new InvalidOperationException("ordinary"), new AggregateException(exception));

        var result = Result.Failure(Error.FromExceptions(aggregate, "Replacement", ErrorCategory.Unavailable));

        Assert.Equal(3, result.Errors.Count);
        Assert.Equal("Replacement", result.Errors[0].UserMessage);
        Assert.Equal(ErrorCategory.Unavailable, result.Errors[0].Category);
        Assert.Same(errors[0], result.Errors[1]);
        Assert.Same(errors[1], result.Errors[2]);
    }
}
