namespace NyssaAI.Configuration.Tests;

public sealed class PublicTypeInventoryTests
{
    [Fact]
    public void Assembly_ExportsOnlyTheTypesAUseCaseWrites()
    {
        // Every exported type is written by a use case in NyssaAI.Configuration.UseCaseTests. The
        // surface is two root contracts, one
        // registration call with its startup options and its OAuth options, the records those
        // contracts name, and the seams an outside package or a test implements.
        // Adding a type here without a use case that writes it is what this test exists to stop.
        string[] expected =
        [
            "NyssaAI.Configuration.Client.BootstrapOptions",
            "NyssaAI.Configuration.Client.ConfigurationServiceCollectionExtensions",
            "NyssaAI.Configuration.Client.NyssaConfigurationOptions",
            "NyssaAI.Configuration.Client.OAuthOptions",
            "NyssaAI.Configuration.Client.VaultOptions",
            "NyssaAI.Configuration.CloudKeyVaults.CloudKeyVaultAction",
            "NyssaAI.Configuration.CloudKeyVaults.CloudKeyVaultEffect",
            "NyssaAI.Configuration.CloudKeyVaults.CloudKeyVaultOperation",
            "NyssaAI.Configuration.CloudKeyVaults.CloudKeyVaultPreparation",
            "NyssaAI.Configuration.CloudKeyVaults.ICloudKeyVaultRecovery",
            "NyssaAI.Configuration.Contracts.ConfigurationMove",
            "NyssaAI.Configuration.Contracts.ConfigurationPointer",
            "NyssaAI.Configuration.Contracts.ConfigurationRemoval",
            "NyssaAI.Configuration.Contracts.IAccessTokenSource",
            "NyssaAI.Configuration.Contracts.IBrowserLauncher",
            "NyssaAI.Configuration.Contracts.ICloudKeyVault",
            "NyssaAI.Configuration.Contracts.ICloudKeyVaultFactory",
            "NyssaAI.Configuration.Contracts.IKeyring",
            "NyssaAI.Configuration.IConfiguration",
            "NyssaAI.Configuration.IConfigurationExtensions",
            "NyssaAI.Configuration.Recovery.ConfigurationOperationCanceledException",
            "NyssaAI.Configuration.Recovery.ConfigurationOperationException",
            "NyssaAI.Configuration.Recovery.ConfigurationOperationKind",
            "NyssaAI.Configuration.Recovery.ConfigurationOperationProgress",
            "NyssaAI.Configuration.Recovery.ConfigurationPublicationState",
            "NyssaAI.Configuration.Recovery.ConfigurationRecoveryHandle",
            "NyssaAI.Configuration.Recovery.ConfigurationRecoveryOutcome",
            "NyssaAI.Configuration.Recovery.ConfigurationRecoveryStatus",
            "NyssaAI.Configuration.Recovery.ConfigurationStepProgress",
            "NyssaAI.Configuration.Recovery.ConfigurationStepState",
        ];

        var actual = typeof(IConfiguration).Assembly.GetExportedTypes()
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }
}
