using System.Globalization;
using NyssaAI.Results.UseCaseTests.fixtures;
using Xunit;

namespace NyssaAI.Results.UseCaseTests;

/// <summary>Compose application validation and translate framework failures at adapter boundaries.</summary>
public sealed class ResultTutorials
{
    /// <summary>Stop at an explicit throwing boundary, retain structured errors, and retry with corrected input.</summary>
    [Fact]
    public void ThrowIfError_ExplicitValidationBoundary_StopsInvalidWorkAndAllowsRecovery()
    {
        // Arrange
        string? customer = null;
        var processed = false;

        // Act
        // ThrowIfError throws on failure; Try preserves the resulting structured errors.
        var failure = Result.Try(() =>
        {
            var name = Guard.Against.Null(customer, nameof(customer)).ThrowIfError();
            Guard.Against.NullOrWhiteSpace(name, nameof(customer)).ThrowIfError();
            processed = true;
        });
        customer = "Ada";
        var validatedCustomer = Guard.Against.NullOrWhiteSpace(customer, nameof(customer)).ThrowIfError();
        var quantity = Guard.Against.OutOfRange(2, 1, 100, "quantity").ThrowIfError();
        Guard
            .Against.True(quantity > 10, "Order cannot be fulfilled", "Stock limit exceeded", ErrorCategory.Conflict)
            .Throw();

        // Assert
        Assert.False(processed);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(failure.Errors).Category);
        Assert.Equal("Ada: 2", $"{validatedCustomer}: {quantity}");
    }

    /// <summary>Validate an order, calculate its total, and render either a receipt or safe correction feedback.</summary>
    [Fact]
    public void Then_OrderValidation_CalculatesAReceiptAndShortCircuitsInvalidInput()
    {
        // Arrange
        const string customer = "Ada";
        const int quantity = 3;
        const decimal unitPrice = 12.50m;
        const int invalidQuantity = 0;

        // Act
        var estimate = Guard
            .Against.NullOrWhiteSpace(customer, nameof(customer))
            .Then(name =>
                Guard
                    .Against.OutOfRange(quantity, 1, 100, nameof(quantity))
                    .Transform(count => (Customer: name, Total: count * unitPrice))
            );
        var receipt = estimate.Resolve(
            order => $"{order.Customer}: {order.Total.ToString("F2", CultureInfo.InvariantCulture)}",
            errors => string.Join("; ", errors.Select(error => error.UserMessage))
        );
        // Transform cannot run for a refused quantity, so downstream calculations need no duplicate guard.
        var rejected = Guard
            .Against.OutOfRange(invalidQuantity, 1, 100, nameof(quantity))
            .Transform(count => (Customer: customer, Total: count * unitPrice));
        var feedback = rejected.Resolve(
            _ => "Order accepted",
            errors => string.Join("; ", errors.Select(error => error.UserMessage))
        );

        // Assert
        Assert.True(estimate.IsSuccess);
        Assert.Equal(37.50m, estimate.Value.Total);
        Assert.Equal("Ada: 37.50", receipt);
        Assert.True(rejected.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(rejected.Errors).Category);
        Assert.NotEqual("Order accepted", feedback);
        Assert.Contains("quantity", feedback, StringComparison.Ordinal);
        // Cleanup
        // Results and guards own no resources.
    }

    /// <summary>Translate a missing local file into an actionable error while separating user and technical messages.</summary>
    [Fact]
    public void Try_MissingImport_ReturnsNotFoundWithoutExposingThePathToTheUser()
    {
        // Arrange
        using (var workspace = new ImportWorkspace())
        {
            // Act
            // File.ReadAllText is a throwing framework boundary; application failures return Result directly.
            var imported = Result.Try(
                () => File.ReadAllText(workspace.InputPath),
                exception =>
                    Error.FromMessages(
                        "Choose an existing import file.",
                        exception.Message,
                        exception is FileNotFoundException ? ErrorCategory.NotFound : ErrorCategory.Unexpected
                    )
            );
            var display = imported.Resolve(
                content => $"Loaded {content.Length} characters.",
                errors => errors[0].UserMessage
            );
            var retryAutomatically =
                imported.IsFailure && imported.Errors.Any(error => error.Category == ErrorCategory.Unavailable);

            // Assert
            Assert.True(imported.IsFailure);
            Assert.Equal(ErrorCategory.NotFound, Assert.Single(imported.Errors).Category);
            Assert.Equal("Choose an existing import file.", display);
            Assert.DoesNotContain(workspace.DirectoryPath, display, StringComparison.Ordinal);
            Assert.Contains("import.txt", imported.Errors[0].TechnicalMessage, StringComparison.Ordinal);
            Assert.False(retryAutomatically);
        }
    }

    /// <summary>Pass a caller's cancellation token through an asynchronous file adapter and stop without retrying.</summary>
    [Fact]
    public async Task TryAsync_CanceledImport_ReportsCanceledAndSkipsSuccessTransformationAsync()
    {
        // Arrange
        using (var workspace = new ImportWorkspace())
        using (var cancellation = new CancellationTokenSource())
        {
            File.WriteAllText(workspace.InputPath, "Useful content");
            cancellation.Cancel();
            var transformed = false;

            // Act
            var read = await Result.TryAsync(
                token => File.ReadAllTextAsync(workspace.InputPath, token),
                cancellation.Token
            );
            var imported = read.Transform(content =>
            {
                transformed = true;
                return content.ToUpperInvariant();
            });
            var status = imported.Resolve(
                _ => "Imported",
                errors =>
                    errors[0].Category == ErrorCategory.Canceled
                        ? "Stopped by the caller; do not retry."
                        : errors[0].UserMessage
            );

            // Assert
            Assert.True(imported.IsFailure);
            Assert.False(transformed);
            Assert.Equal(ErrorCategory.Canceled, Assert.Single(imported.Errors).Category);
            Assert.Equal("Stopped by the caller; do not retry.", status);
            Assert.Equal("Useful content", File.ReadAllText(workspace.InputPath));
        }
    }
}
