using Xunit;

namespace NyssaAI.Results.Tests;

// This file owns the guards whose behaviour is not a boundary: NotEmpty, Matches, both Condition
// overloads, the success arms of NotNull / NotNullOrEmpty / NotNullOrWhiteSpace, and the chain
// composition tests. Every boundary case (InRange, GreaterThan, GreaterThanOrEqual) and every
// null / empty / whitespace failure, together with the sweep proving each guard reports
// Validation, lives in GuardBoundaryTests.cs — so a behaviour is written once, in one place.
public sealed class GuardTests
{
    // The three string guards differ only in what they reject; on a value they all behave the
    // same way, which is one behaviour with three inputs rather than three facts.
    [Theory]
    [InlineData("NotNull")]
    [InlineData("NotNullOrEmpty")]
    [InlineData("NotNullOrWhiteSpace")]
    public void StringGuards_WithAValue_ReturnTheValueUnchanged(string guard)
    {
        var result = guard switch
        {
            "NotNull" => Guard.Against.Null("test", "username"),
            "NotNullOrEmpty" => Guard.Against.NullOrEmpty("test", "name"),
            _ => Guard.Against.NullOrWhiteSpace("test", "name"),
        };

        Assert.True(result.IsSuccess);
        Assert.Equal("test", result.Value);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("null")]
    public void NotEmpty_WithNoItems_ReturnsValidationFailure(string collection)
    {
        var result = collection switch
        {
            "empty" => Guard.Against.NullOrEmpty(new List<int>(), "items"),
            _ => Guard.Against.NullOrEmpty<int>(null, "items"),
        };

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void NotEmpty_WithItems_ReturnsSuccess()
    {
        var items = new[] { 1, 2, 3 };
        var result = Guard.Against.NullOrEmpty(items, "items");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
    }

    [Fact]
    public void Matches_WithMatchingPattern_ReturnsSuccess()
    {
        var result = Guard.Against.PatternMismatch("test@example.com", @"^[^@]+@[^@]+\.[^@]+$", "email");

        Assert.True(result.IsSuccess);
        Assert.Equal("test@example.com", result.Value);
    }

    [Fact]
    public void Matches_WithNonMatchingPattern_ReturnsFailure()
    {
        var result = Guard.Against.PatternMismatch("invalid-email", @"^[^@]+@[^@]+\.[^@]+$", "email");

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Validation, error.Category);
        Assert.Contains("email", error.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Condition_WhenTrue_ReturnsSuccess()
    {
        var result = Guard.Against.False(true, "Condition failed");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Condition_WhenFalse_ReturnsFailure()
    {
        var result = Guard.Against.False(false, "Condition failed");

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Validation, error.Category);
        Assert.Equal("Condition failed", error.UserMessage);
    }

    [Fact]
    public void ConditionGeneric_WhenPredicateTrue_ReturnsSuccess()
    {
        var result = Guard.Against.False(5, x => x > 0, "Value must be positive");

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value);
    }

    [Fact]
    public void ConditionGeneric_WhenPredicateFalse_ReturnsFailure()
    {
        var result = Guard.Against.False(5, x => x < 0, "Value must be negative");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void Guards_InValidationChain_CombinesMultipleChecks()
    {
        var result = Guard.Against.NullOrEmpty("test", "username")
            .Then(username => Guard.Against.LessThan(username.Length, 3, "username length"))
            .Then(_ => Guard.Against.Null("valid", "email"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Guards_InValidationChain_StopsOnFirstFailure()
    {
        var result = Guard.Against.NullOrEmpty("", "username")
            .Then(_ => Guard.Against.Null("valid", "email"));

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Validation, error.Category);
        Assert.Contains("username", error.UserMessage, StringComparison.Ordinal);
    }
}
