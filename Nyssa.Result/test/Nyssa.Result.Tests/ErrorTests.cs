using System.Runtime.CompilerServices;
using Xunit;

namespace NyssaAI.Results.Tests;

public sealed class ErrorTests
{
    [Fact]
    public void FromExceptions_Sequence_ReturnsErrorsThatCannotBeCleared()
    {
        var errors = Error.FromExceptions(new Exception[] { new IOException("File unavailable") });

        var collection = Assert.IsAssignableFrom<ICollection<Error>>(errors);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Single(errors);
    }

    [Fact]
    public void FromMessages_WithSeparateMessages_KeepsBoth()
    {
        var error = Error.FromMessages("User friendly message", "Technical: invalid data format");

        Assert.Equal("User friendly message", error.UserMessage);
        Assert.Equal("Technical: invalid data format", error.TechnicalMessage);
    }

    [Fact]
    public void FromMessage_WithOneMessage_UsesItForBothContexts()
    {
        var error = Error.FromMessage("An error occurred");

        Assert.Equal("An error occurred", error.UserMessage);
        Assert.Equal("An error occurred", error.TechnicalMessage);
    }

    [Theory]
    [InlineData("User message", "Technical message", true)]
    [InlineData("Different user", "Technical message", false)]
    [InlineData("User message", "Different technical", false)]
    public void Equals_WithMatchingOrDifferingMessages_ReportsValueEquality(
        string userMessage,
        string technicalMessage,
        bool expected)
    {
        var first = Error.FromMessages("User message", "Technical message");
        var second = Error.FromMessages(userMessage, technicalMessage);

        Assert.Equal(expected, first == second);
    }

    [Fact]
    public void Equals_WithMatchingMessages_ProducesMatchingHashCodes()
    {
        var first = Error.FromMessages("User message", "Technical message");
        var second = Error.FromMessages("User message", "Technical message");

        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void StackTrace_WhenBuiltFromMessages_IsNull()
    {
        var error = Error.FromMessages("User", "Technical");

        Assert.Null(error.StackTrace);
    }

    [Fact]
    public void StackTrace_WhenBuiltFromAThrownException_IsCaptured()
    {
        var error = Error.FromException(Caught(ThrowInvalidOperation), "Something went wrong.");

        Assert.NotNull(error.StackTrace);
        Assert.Contains(nameof(ThrowInvalidOperation), error.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void Equals_WithIdenticalMessagesFromDifferentThrowSites_ReportsInequality()
    {
        var first = Error.FromException(Caught(ThrowInvalidOperation), "User");
        var second = Error.FromException(Caught(ThrowInvalidOperationElsewhere), "User");

        // Both helpers throw the same type with the same message, so only the captured frames
        // differ -- which is what makes this an assertion about StackTrace and nothing else.
        Assert.Equal(first.UserMessage, second.UserMessage);
        Assert.Equal(first.TechnicalMessage, second.TechnicalMessage);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ToString_WithACapturedStackTrace_ReportsCaptureWithoutInliningTheFrames()
    {
        var error = Error.FromException(Caught(ThrowInvalidOperation), "User");

        var text = error.ToString();

        Assert.Contains("StackTrace = (captured)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ThrowInvalidOperation), text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_WithoutAStackTrace_OmitsTheStackTraceMember()
    {
        var text = Error.FromMessages("User", "Technical").ToString();

        Assert.Contains("UserMessage = User", text, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", text, StringComparison.Ordinal);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidOperation() => throw new InvalidOperationException("boom");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidOperationElsewhere() =>
        throw new InvalidOperationException("boom");

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

    [Fact]
    public void FromMessage_WithANullMessage_FallsBackToTheDefaultUserMessage()
    {
        var error = Error.FromMessage(null);

        Assert.Equal(Error.DefaultUserMessage, error.UserMessage);
        Assert.Equal(Error.DefaultUserMessage, error.TechnicalMessage);
    }

    [Theory]
    [InlineData(null, null, Error.DefaultUserMessage, Error.DefaultUserMessage)]
    [InlineData("user", null, "user", "user")]
    [InlineData(null, "tech", Error.DefaultUserMessage, "tech")]
    public void FromMessages_WithNullMessages_NormalizesRatherThanStoringNull(
        string? userMessage,
        string? technicalMessage,
        string expectedUser,
        string expectedTechnical)
    {
        var error = Error.FromMessages(userMessage, technicalMessage);

        Assert.Equal(expectedUser, error.UserMessage);
        Assert.Equal(expectedTechnical, error.TechnicalMessage);
        Assert.Contains("UserMessage = ", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FromMessage_WithAnUndefinedCategory_FallsBackToUnexpected()
    {
        var error = Error.FromMessage("x", (ErrorCategory)999);

        Assert.Equal(ErrorCategory.Unexpected, error.Category);
    }

    [Fact]
    public void FromMessages_WithIndeterminateOutcome_PreservesCategoryThroughResultComposition()
    {
        // Arrange: value 7 is the additive category; existing numeric values must not shift.
        var category = ErrorCategory.Indeterminate;
        var error = Error.FromMessages("Outcome unknown.", "Commit was not acknowledged.", category);

        // Act
        var result = Result<int>.Failure(error).Then(value => Result<string>.Success(value.ToString()));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(category, Assert.Single(result.Errors).Category);
        Assert.Equal(6, (int)ErrorCategory.Canceled);
        Assert.Equal(7, (int)ErrorCategory.Indeterminate);
    }

    [Fact]
    public void ToString_WithANonDefaultCategory_NamesIt()
    {
        var text = Error.FromMessage("x", ErrorCategory.NotFound).ToString();

        Assert.Contains("Category = NotFound", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_WithTheDefaultCategory_OmitsIt()
    {
        Assert.DoesNotContain("Category", Error.FromMessage("x").ToString(), StringComparison.Ordinal);
    }
}
