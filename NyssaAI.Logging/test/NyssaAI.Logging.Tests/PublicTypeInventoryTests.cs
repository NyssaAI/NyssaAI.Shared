using Xunit;

namespace NyssaAI.Logging.Tests;

public sealed class PublicTypeInventoryTests
{
    [Fact]
    public void Assembly_ExportsOnlyTheRegistrationExtensions()
    {
        // The package publishes one destination for Microsoft logging and one call that configures
        // logging around it, and nothing else: no logger contract, no options record, no
        // construction API. Every exported member is written by a use case in
        // NyssaAI.Logging.UseCaseTests, and adding one here without a use case that writes it is what
        // this test exists to stop.
        string[] expected =
        [
            "NyssaAI.Logging.Configuration.LoggingBuilderExtensions",
            "NyssaAI.Logging.Configuration.LoggingServiceCollectionExtensions"
        ];

        var actual = typeof(Configuration.LoggingBuilderExtensions).Assembly.GetExportedTypes()
            .Select(type => type.FullName!);

        Assert.Equal(expected, actual.Order(StringComparer.Ordinal));
    }
}
