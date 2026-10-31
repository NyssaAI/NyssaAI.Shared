using System.Text.Json;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>
/// Spins up disposable Linux VM containers to verify that the C#, TypeScript, and Python
/// configuration libraries place manifests, namespace files, and OS keyring entries in the exact
/// expected default and custom locations, write the expected on-disk JSON and encrypted payloads,
/// and interoperate seamlessly.
/// </summary>
public sealed class PlatformFilesystemLayoutVmTests
{
    private const string ApplicationName = "AcmeCommerce";
    private const string DefaultConfigDirectory = "/home/svc_acme/.nyssa-ai/AcmeCommerce";

    [Fact]
    public void DefaultLocations_CrossLanguageComponents_PlaceFilesAndKeyringEntriesAtExpectedLocationsAndInterop()
    {
        // Arrange
        using (var vm = new LinuxVmWorkstation())
        {
            // Act
            // 1. C# component (checkout-api) runs first with default locations
            vm.WriteWithCSharp(ApplicationName, "checkout-api", "ListenPort", "8443");
            vm.WriteWithCSharp(ApplicationName, "checkout-api", "StripeWebhookSigningSecret", "whsec_live_cs_secret_11", provider: "aesLocal");

            // 2. TypeScript component (fulfillment-worker) runs with default locations, reads C#, writes its own
            var tsReadCsPlain = vm.ReadWithTypeScript(ApplicationName, "checkout-api", "ListenPort");
            var tsReadCsSecret = vm.ReadWithTypeScript(ApplicationName, "checkout-api", "StripeWebhookSigningSecret");
            vm.WriteWithTypeScript(ApplicationName, "fulfillment-worker", "BatchSize", "25");
            vm.WriteWithTypeScript(ApplicationName, "fulfillment-worker", "WarehouseApiKey", "wh_live_ts_secret_22", provider: "aesLocal");

            // 3. Python component (inventory-sync) runs with default locations, reads C# & TS, writes its own
            var pyReadCsSecret = vm.ReadWithPython(ApplicationName, "checkout-api", "StripeWebhookSigningSecret");
            var pyReadTsPlain = vm.ReadWithPython(ApplicationName, "fulfillment-worker", "BatchSize");
            var pyReadTsSecret = vm.ReadWithPython(ApplicationName, "fulfillment-worker", "WarehouseApiKey");
            vm.WriteWithPython(ApplicationName, "inventory-sync", "SyncIntervalSeconds", "60");
            vm.WriteWithPython(ApplicationName, "inventory-sync", "ErpToken", "erp_live_py_secret_33", provider: "aesLocal");

            // 4. C# and TypeScript read back values written by the other components
            var csReadTsSecret = vm.ReadWithCSharp(ApplicationName, "fulfillment-worker", "WarehouseApiKey");
            var csReadPyPlain = vm.ReadWithCSharp(ApplicationName, "inventory-sync", "SyncIntervalSeconds");
            var csReadPySecret = vm.ReadWithCSharp(ApplicationName, "inventory-sync", "ErpToken");
            var tsReadPySecret = vm.ReadWithTypeScript(ApplicationName, "inventory-sync", "ErpToken");

            // Assert — Cross-language round-trip values
            Assert.Equal("8443", tsReadCsPlain);
            Assert.Equal("whsec_live_cs_secret_11", tsReadCsSecret);
            Assert.Equal("whsec_live_cs_secret_11", pyReadCsSecret);
            Assert.Equal("25", pyReadTsPlain);
            Assert.Equal("wh_live_ts_secret_22", pyReadTsSecret);
            Assert.Equal("wh_live_ts_secret_22", csReadTsSecret);
            Assert.Equal("60", csReadPyPlain);
            Assert.Equal("erp_live_py_secret_33", csReadPySecret);
            Assert.Equal("erp_live_py_secret_33", tsReadPySecret);

            // Assert — Expected default paths and POSIX permissions (0700 directories, 0600 files)
            Assert.True(vm.DirectoryExists("/home/svc_acme/.nyssa-ai"));
            Assert.Equal("700", vm.ModeOctal("/home/svc_acme/.nyssa-ai"));
            Assert.True(vm.FileExists(LinuxVmWorkstation.DefaultManifestPath));
            Assert.Equal("600", vm.ModeOctal(LinuxVmWorkstation.DefaultManifestPath));
            Assert.True(vm.DirectoryExists(DefaultConfigDirectory));
            Assert.Equal("700", vm.ModeOctal(DefaultConfigDirectory));

            var checkoutFile = $"{DefaultConfigDirectory}/checkout-api.json";
            var fulfillmentFile = $"{DefaultConfigDirectory}/fulfillment-worker.json";
            var inventoryFile = $"{DefaultConfigDirectory}/inventory-sync.json";
            Assert.True(vm.FileExists(checkoutFile));
            Assert.True(vm.FileExists(fulfillmentFile));
            Assert.True(vm.FileExists(inventoryFile));
            Assert.Equal("600", vm.ModeOctal(checkoutFile));
            Assert.Equal("600", vm.ModeOctal(fulfillmentFile));
            Assert.Equal("600", vm.ModeOctal(inventoryFile));

            // Assert — OS Keyring entry at service="nyssa-ai-configuration", account="AcmeCommerce"
            var masterKeyBase64 = vm.ReadKeyringSecret(ConfigurationServiceCollectionExtensions.KeyringService, ApplicationName);
            Assert.Equal(32, Convert.FromBase64String(masterKeyBase64).Length);

            // Assert — On-disk manifest JSON and namespace JSON contents
            AssertManifestSection(vm.ReadFile(LinuxVmWorkstation.DefaultManifestPath), ApplicationName, DefaultConfigDirectory, 10000);
            AssertPlaintextEntry(vm.ReadFile(checkoutFile), "ListenPort", "8443");
            AssertEncryptedEntry(vm.ReadFile(checkoutFile), "StripeWebhookSigningSecret", "whsec_live_cs_secret_11", "aesLocal");
            AssertPlaintextEntry(vm.ReadFile(fulfillmentFile), "BatchSize", "25");
            AssertEncryptedEntry(vm.ReadFile(fulfillmentFile), "WarehouseApiKey", "wh_live_ts_secret_22", "aesLocal");
            AssertPlaintextEntry(vm.ReadFile(inventoryFile), "SyncIntervalSeconds", "60");
            AssertEncryptedEntry(vm.ReadFile(inventoryFile), "ErpToken", "erp_live_py_secret_33", "aesLocal");
        }
    }

    [Fact]
    public void CustomLocations_CrossLanguageComponents_PlaceFilesAtCustomPathsAndLeaveDefaultPathsUntouched()
    {
        // Arrange
        using (var vm = new LinuxVmWorkstation())
        {
            const string customManifestPath = "/home/svc_acme/deploy/custom-manifest.json";
            const string customConfigDirectory = "/home/svc_acme/custom-store/acme-commerce";

            // Act
            // 1. C# component provisions the custom manifest and custom configuration directory
            vm.WriteWithCSharp(ApplicationName, "checkout-api", "ListenPort", "9443", manifestPath: customManifestPath, configDirectory: customConfigDirectory);
            vm.WriteWithCSharp(ApplicationName, "checkout-api", "StripeWebhookSigningSecret", "whsec_custom_cs_secret_44", provider: "aesLocal", manifestPath: customManifestPath, configDirectory: customConfigDirectory);

            // 2. TypeScript points only at customManifestPath, resolves customConfigDirectory from manifest
            var tsReadCsPlain = vm.ReadWithTypeScript(ApplicationName, "checkout-api", "ListenPort", manifestPath: customManifestPath);
            var tsReadCsSecret = vm.ReadWithTypeScript(ApplicationName, "checkout-api", "StripeWebhookSigningSecret", manifestPath: customManifestPath);
            vm.WriteWithTypeScript(ApplicationName, "fulfillment-worker", "BatchSize", "50", manifestPath: customManifestPath);
            vm.WriteWithTypeScript(ApplicationName, "fulfillment-worker", "WarehouseApiKey", "wh_custom_ts_secret_55", provider: "aesLocal", manifestPath: customManifestPath);

            // 3. Python points only at customManifestPath, resolves customConfigDirectory from manifest
            var pyReadCsSecret = vm.ReadWithPython(ApplicationName, "checkout-api", "StripeWebhookSigningSecret", manifestPath: customManifestPath);
            var pyReadTsSecret = vm.ReadWithPython(ApplicationName, "fulfillment-worker", "WarehouseApiKey", manifestPath: customManifestPath);
            vm.WriteWithPython(ApplicationName, "inventory-sync", "SyncIntervalSeconds", "120", manifestPath: customManifestPath);
            vm.WriteWithPython(ApplicationName, "inventory-sync", "ErpToken", "erp_custom_py_secret_66", provider: "aesLocal", manifestPath: customManifestPath);

            // 4. C# reads back TypeScript and Python entries via customManifestPath
            var csReadTsSecret = vm.ReadWithCSharp(ApplicationName, "fulfillment-worker", "WarehouseApiKey", manifestPath: customManifestPath);
            var csReadPySecret = vm.ReadWithCSharp(ApplicationName, "inventory-sync", "ErpToken", manifestPath: customManifestPath);

            // Assert — Default locations were never created
            Assert.False(vm.FileExists(LinuxVmWorkstation.DefaultManifestPath));
            Assert.False(vm.DirectoryExists(DefaultConfigDirectory));

            // Assert — Custom locations exist with expected permissions
            Assert.True(vm.FileExists(customManifestPath));
            Assert.Equal("600", vm.ModeOctal(customManifestPath));
            Assert.True(vm.DirectoryExists(customConfigDirectory));
            Assert.Equal("700", vm.ModeOctal(customConfigDirectory));

            var checkoutFile = $"{customConfigDirectory}/checkout-api.json";
            var fulfillmentFile = $"{customConfigDirectory}/fulfillment-worker.json";
            var inventoryFile = $"{customConfigDirectory}/inventory-sync.json";
            Assert.True(vm.FileExists(checkoutFile));
            Assert.True(vm.FileExists(fulfillmentFile));
            Assert.True(vm.FileExists(inventoryFile));
            Assert.Equal("600", vm.ModeOctal(checkoutFile));
            Assert.Equal("600", vm.ModeOctal(fulfillmentFile));
            Assert.Equal("600", vm.ModeOctal(inventoryFile));

            // Assert — Cross-language round-trip values and on-disk JSON
            Assert.Equal("9443", tsReadCsPlain);
            Assert.Equal("whsec_custom_cs_secret_44", tsReadCsSecret);
            Assert.Equal("whsec_custom_cs_secret_44", pyReadCsSecret);
            Assert.Equal("wh_custom_ts_secret_55", pyReadTsSecret);
            Assert.Equal("wh_custom_ts_secret_55", csReadTsSecret);
            Assert.Equal("erp_custom_py_secret_66", csReadPySecret);

            AssertManifestSection(vm.ReadFile(customManifestPath), ApplicationName, customConfigDirectory, 10000);
            AssertPlaintextEntry(vm.ReadFile(checkoutFile), "ListenPort", "9443");
            AssertEncryptedEntry(vm.ReadFile(checkoutFile), "StripeWebhookSigningSecret", "whsec_custom_cs_secret_44", "aesLocal");
            AssertPlaintextEntry(vm.ReadFile(fulfillmentFile), "BatchSize", "50");
            AssertEncryptedEntry(vm.ReadFile(fulfillmentFile), "WarehouseApiKey", "wh_custom_ts_secret_55", "aesLocal");
            AssertPlaintextEntry(vm.ReadFile(inventoryFile), "SyncIntervalSeconds", "120");
            AssertEncryptedEntry(vm.ReadFile(inventoryFile), "ErpToken", "erp_custom_py_secret_66", "aesLocal");
        }
    }

    [Fact]
    public void DefaultLocations_TypeScriptFirstRun_ProvisionsIdenticalDefaultLayoutForCSharpAndPython()
    {
        // Arrange
        using (var vm = new LinuxVmWorkstation())
        {
            // Act — TypeScript initializes an unprovisioned VM first using defaults
            vm.WriteWithTypeScript(ApplicationName, "bootstrap", "InitialSecret", "provisioned-by-ts", provider: "aesLocal");
            var csReadValue = vm.ReadWithCSharp(ApplicationName, "bootstrap", "InitialSecret");
            var pyReadValue = vm.ReadWithPython(ApplicationName, "bootstrap", "InitialSecret");

            // Assert
            Assert.Equal("provisioned-by-ts", csReadValue);
            Assert.Equal("provisioned-by-ts", pyReadValue);
            Assert.Equal("700", vm.ModeOctal("/home/svc_acme/.nyssa-ai"));
            Assert.Equal("600", vm.ModeOctal(LinuxVmWorkstation.DefaultManifestPath));
            Assert.Equal("700", vm.ModeOctal(DefaultConfigDirectory));
            Assert.Equal("600", vm.ModeOctal($"{DefaultConfigDirectory}/bootstrap.json"));
            AssertManifestSection(vm.ReadFile(LinuxVmWorkstation.DefaultManifestPath), ApplicationName, DefaultConfigDirectory, 10000);
            AssertEncryptedEntry(vm.ReadFile($"{DefaultConfigDirectory}/bootstrap.json"), "InitialSecret", "provisioned-by-ts", "aesLocal");
        }
    }

    [Fact]
    public void DefaultLocations_PythonFirstRun_ProvisionsIdenticalDefaultLayoutForCSharpAndTypeScript()
    {
        // Arrange
        using (var vm = new LinuxVmWorkstation())
        {
            // Act — Python initializes an unprovisioned VM first using defaults
            vm.WriteWithPython(ApplicationName, "bootstrap", "InitialSecret", "provisioned-by-py", provider: "aesLocal");
            var csReadValue = vm.ReadWithCSharp(ApplicationName, "bootstrap", "InitialSecret");
            var tsReadValue = vm.ReadWithTypeScript(ApplicationName, "bootstrap", "InitialSecret");

            // Assert
            Assert.Equal("provisioned-by-py", csReadValue);
            Assert.Equal("provisioned-by-py", tsReadValue);
            Assert.Equal("700", vm.ModeOctal("/home/svc_acme/.nyssa-ai"));
            Assert.Equal("600", vm.ModeOctal(LinuxVmWorkstation.DefaultManifestPath));
            Assert.Equal("700", vm.ModeOctal(DefaultConfigDirectory));
            Assert.Equal("600", vm.ModeOctal($"{DefaultConfigDirectory}/bootstrap.json"));
            AssertManifestSection(vm.ReadFile(LinuxVmWorkstation.DefaultManifestPath), ApplicationName, DefaultConfigDirectory, 10000);
            AssertEncryptedEntry(vm.ReadFile($"{DefaultConfigDirectory}/bootstrap.json"), "InitialSecret", "provisioned-by-py", "aesLocal");
        }
    }

    private static void AssertManifestSection(string manifestJson, string applicationName, string expectedConfigDir, int expectedLockTimeoutMs)
    {
        using (var doc = JsonDocument.Parse(manifestJson))
        {
            Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
            var section = doc.RootElement.GetProperty("applications").GetProperty(applicationName);
            Assert.Equal(expectedConfigDir, section.GetProperty("configurationDirectory").GetString());
            Assert.Equal(expectedLockTimeoutMs, section.GetProperty("lockTimeoutMs").GetInt32());
        }
    }

    private static void AssertPlaintextEntry(string namespaceJson, string key, string expectedValue)
    {
        using (var doc = JsonDocument.Parse(namespaceJson))
        {
            var entry = doc.RootElement.GetProperty(key);
            Assert.Equal(expectedValue, entry.GetProperty("value").GetString());
            Assert.False(entry.TryGetProperty("encrypted", out _));
        }
    }

    private static void AssertEncryptedEntry(string namespaceJson, string key, string plaintextSecret, string expectedProvider)
    {
        Assert.DoesNotContain(plaintextSecret, namespaceJson, StringComparison.Ordinal);
        using (var doc = JsonDocument.Parse(namespaceJson))
        {
            var entry = doc.RootElement.GetProperty(key);
            Assert.True(entry.GetProperty("encrypted").GetBoolean());
            Assert.Equal(expectedProvider, entry.GetProperty("provider").GetString());
            var ciphertext = entry.GetProperty("value").GetString();
            Assert.False(string.IsNullOrWhiteSpace(ciphertext));
            Assert.DoesNotContain(plaintextSecret, ciphertext!, StringComparison.Ordinal);
        }
    }
}
