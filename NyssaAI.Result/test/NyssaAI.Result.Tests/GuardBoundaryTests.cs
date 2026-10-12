using Xunit;

namespace NyssaAI.Results.Tests;

// The comparison guards differ only in their boundary condition, so the boundary is what these
// pin: below, at, and above each bound, as one behaviour with several inputs rather than one fact
// per case. An off-by-one at a bound is the defect this shape catches.
//
// This file owns those boundaries — InRange, GreaterThan, GreaterThanOrEqual — plus every guard
// failure and the category it reports: the null / empty / whitespace refusals, the Matches and
// Condition failure modes that must not throw, and the sweep proving each guard reports
// Validation rather than Unexpected. GuardTests.cs owns the rest: NotEmpty, Matches against a
// pattern, both Condition overloads, the success arms of the string guards, and chain composition.
public sealed class GuardBoundaryTests
{
    [Theory]
    [InlineData(1, 1, 10)]   // at the lower bound
    [InlineData(5, 1, 10)]   // inside
    [InlineData(10, 1, 10)]  // at the upper bound
    [InlineData(7, 7, 7)]    // degenerate range
    public void InRange_WithinInclusiveBounds_Succeeds(int value, int min, int max)
    {
        var result = Guard.Against.OutOfRange(value, min, max, "count");

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value);
    }

    [Theory]
    [InlineData(0, 1, 10)]
    [InlineData(11, 1, 10)]
    [InlineData(-1, 0, 0)]
    public void InRange_OutsideBounds_ReturnsValidationFailure(int value, int min, int max)
    {
        var result = Guard.Against.OutOfRange(value, min, max, "count");

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Validation, error.Category);
        Assert.Contains("count", error.UserMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(5, 3)]
    [InlineData(int.MaxValue, 0)]
    public void GreaterThan_StrictlyAbove_Succeeds(int value, int min)
    {
        var result = Guard.Against.LessThanOrEqual(value, min, "count");

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value);
    }

    [Theory]
    [InlineData(1, 1)]  // equal is not strictly greater
    [InlineData(5, 5)]
    [InlineData(0, 1)]
    [InlineData(3, 5)]
    public void GreaterThan_AtOrBelow_ReturnsValidationFailure(int value, int min)
    {
        var result = Guard.Against.LessThanOrEqual(value, min, "count");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
    }

    [Theory]
    [InlineData(1, 1)]  // equal is allowed here, and this is the only difference from GreaterThan
    [InlineData(5, 5)]
    [InlineData(2, 1)]
    [InlineData(5, 3)]
    public void GreaterThanOrEqual_AtOrAbove_Succeeds(int value, int min)
    {
        var result = Guard.Against.LessThan(value, min, "count");

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 5)]
    public void GreaterThanOrEqual_Below_ReturnsValidationFailure(int value, int min)
    {
        var result = Guard.Against.LessThan(value, min, "count");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void NotNull_WithNull_ReturnsValidationFailure()
    {
        var result = Guard.Against.Null<string>(null, "username");

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Validation, error.Category);
        Assert.Contains("username", error.UserMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NotNullOrEmpty_WithNothing_ReturnsValidationFailure(string? value)
    {
        var result = Guard.Against.NullOrEmpty(value, "name");

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Validation, error.Category);
        Assert.Contains("name", error.UserMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void NotNullOrEmpty_WithWhitespace_Succeeds(string value)
    {
        // Whitespace is not empty; NotNullOrWhiteSpace is the guard that refuses it.
        Assert.True(Guard.Against.NullOrEmpty(value, "name").IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void NotNullOrWhiteSpace_WithNothingMeaningful_ReturnsValidationFailure(string? value)
    {
        var result = Guard.Against.NullOrWhiteSpace(value, "name");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
    }

    // Guard exists to turn a precondition check into a categorised failure. Before this round
    // every one of them produced Unexpected, the category meaning "we do not know what went
    // wrong", so a caller could not tell bad input from a defect.
    [Theory]
    [InlineData("NotNull")]
    [InlineData("NotNullOrEmpty")]
    [InlineData("NotNullOrWhiteSpace")]
    [InlineData("NotEmpty")]
    [InlineData("InRange")]
    [InlineData("GreaterThan")]
    [InlineData("GreaterThanOrEqual")]
    [InlineData("Matches")]
    [InlineData("Condition")]
    [InlineData("ConditionOfT")]
    public void Guards_WithAFailingPrecondition_ReportValidationRatherThanUnexpected(string guard)
    {
        IReadOnlyList<Error> errors = guard switch
        {
            "NotNull" => Guard.Against.Null<string>(null, "a").Errors,
            "NotNullOrEmpty" => Guard.Against.NullOrEmpty(null, "a").Errors,
            "NotNullOrWhiteSpace" => Guard.Against.NullOrWhiteSpace(null, "a").Errors,
            "NotEmpty" => Guard.Against.NullOrEmpty<int>(null, "a").Errors,
            "InRange" => Guard.Against.OutOfRange(0, 1, 2, "a").Errors,
            "GreaterThan" => Guard.Against.LessThanOrEqual(0, 1, "a").Errors,
            "GreaterThanOrEqual" => Guard.Against.LessThan(0, 1, "a").Errors,
            "Matches" => Guard.Against.PatternMismatch("x", "^y$", "a").Errors,
            "Condition" => Guard.Against.False(false, "a").Errors,
            _ => Guard.Against.False(0, _ => false, "a").Errors,
        };

        // Assert.Single rather than Errors[0]: a guard that wrongly succeeded reports an empty
        // collection here instead of throwing IndexOutOfRangeException.
        Assert.Equal(ErrorCategory.Validation, Assert.Single(errors).Category);
    }

    [Fact]
    public void Matches_WithANullValue_ReturnsAFailureRatherThanThrowing()
    {
        var result = Guard.Against.PatternMismatch(null, "^a$", "name");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void Matches_WithAnInvalidPattern_ReturnsAFailureRatherThanThrowing()
    {
        var result = Guard.Against.PatternMismatch("abc", "([unclosed", "name");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void Matches_WithACatastrophicallyBacktrackingPattern_TimesOutInsteadOfHanging()
    {
        // (a+)+$ against a long non-matching input is the canonical runaway. The bounded match
        // timeout is what keeps a caller-supplied pattern from consuming a core indefinitely.
        var result = Guard.Against.PatternMismatch(new string('a', 40) + "!", "^(a+)+$", "name");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Unavailable, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void Condition_WithANonDefaultCategory_ReportsThatCategory()
    {
        var result = Guard.Against.False(false, "conflict", technicalMessage: null, ErrorCategory.Conflict);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Conflict, Assert.Single(result.Errors).Category);
    }

    // T : IComparable<T> admits reference types, so a null value must be a validation failure
    // rather than a NullReferenceException escaping a guard.
    [Theory]
    [InlineData("InRange")]
    [InlineData("GreaterThan")]
    [InlineData("GreaterThanOrEqual")]
    public void ComparisonGuards_WithANullReference_ReturnValidationFailureRatherThanThrowing(string guard)
    {
        var result = guard switch
        {
            "InRange" => Guard.Against.OutOfRange<string>(null!, "a", "z", "code"),
            "GreaterThan" => Guard.Against.LessThanOrEqual<string>(null!, "a", "code"),
            _ => Guard.Against.LessThan<string>(null!, "a", "code"),
        };

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Validation, error.Category);
        Assert.Contains("code is null", error.TechnicalMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("InRange")]
    [InlineData("GreaterThan")]
    [InlineData("GreaterThanOrEqual")]
    public void ComparisonGuards_WithAReferenceValue_StillCompare(string guard)
    {
        var result = guard switch
        {
            "InRange" => Guard.Against.OutOfRange("m", "a", "z", "code"),
            "GreaterThan" => Guard.Against.LessThanOrEqual("b", "a", "code"),
            _ => Guard.Against.LessThan("a", "a", "code"),
        };

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Condition_WithAThrowingPredicate_ReturnsAFailureRatherThanThrowing()
    {
        var result = Guard.Against.False(5, _ => throw new InvalidOperationException("predicate"), "must hold");

        Assert.True(result.IsFailure);
        Assert.Contains("predicate", Assert.Single(result.Errors).TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void NotEmpty_WithAThrowingSequence_ReturnsAFailureRatherThanThrowing()
    {
        static IEnumerable<int> Explode()
        {
            yield return 1;
            throw new InvalidOperationException("sequence");
        }

        var result = Guard.Against.NullOrEmpty(Explode(), "items");

        Assert.True(result.IsFailure);
        Assert.Contains("sequence", Assert.Single(result.Errors).TechnicalMessage, StringComparison.Ordinal);
    }
}
