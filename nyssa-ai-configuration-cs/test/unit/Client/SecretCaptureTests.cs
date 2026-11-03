using System.Net;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Encryption;
using NyssaAI.Configuration.OAuth.Infrastructure;
using NyssaAI.Configuration.SecretInput;
using NyssaAI.Configuration.Tests.Support;
using NyssaAI.Configuration.Values;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.Client;

public sealed class SecretCaptureTests
{
    [Fact]
    public async Task CaptureSecretAsync_StoresEncryptedAndReturnsSanitizedReceipt()
    {
        using var directory = new TemporaryDirectory();
        var documents = new ConfigurationDocumentFactoryLocalFile(directory.Path);
        var cipher = ConfigurationCipherAesGcm.Create(new byte[32]).Value;
        var localEncryption = new ConfigurationProviderLocalEncryption(cipher);
        var store = ConfigurationStore.Create(documents, [localEncryption]).Value;
        using var client = new ConfigurationClient(store);

        var mockInput = new MockSecretInputProvider(new CapturedSecret("s3cretP@ss".ToCharArray(), "aesLocal"));
        var options = new CaptureSecretOptions
        {
            PromptMessage = "Enter database password",
            PreferredProvider = "aesLocal",
            InputProvider = mockInput
        };

        var extensions = (IConfigurationExtensions)client;
        var result = await extensions.CaptureSecretAsync("database", "password", options, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var receipt = result.Value;
        Assert.Equal("database", receipt.Namespace);
        Assert.Equal("password", receipt.Key);
        Assert.Equal("aesLocal", receipt.Provider);

        // Verify stored value in file is sealed/encrypted, not plaintext
        var rawJson = await File.ReadAllTextAsync(Path.Combine(directory.Path, "database.json"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("s3cretP@ss", rawJson, StringComparison.Ordinal);
        Assert.Contains("\"provider\": \"aesLocal\"", rawJson, StringComparison.Ordinal);
        Assert.Contains("\"encrypted\": true", rawJson, StringComparison.Ordinal);

        // Verify normal read can decrypt it
        var decrypted = await client.GetAsync("database", "password", TestContext.Current.CancellationToken);
        Assert.Equal("s3cretP@ss", decrypted);
    }

    [Fact]
    public async Task CaptureSecretAsync_WhenForcedProviderIsSet_LocksDestinationToForcedProvider()
    {
        using var directory = new TemporaryDirectory();
        var documents = new ConfigurationDocumentFactoryLocalFile(directory.Path);
        var cipher = ConfigurationCipherAesGcm.Create(new byte[32]).Value;
        var localEncryption = new ConfigurationProviderLocalEncryption(cipher);
        var store = ConfigurationStore.Create(documents, [localEncryption]).Value;
        using var client = new ConfigurationClient(store);

        SecretPromptRequest? capturedReq = null;
        var mockInput = new MockSecretInputProvider(req =>
        {
            capturedReq = req;
            return Result<CapturedSecret>.Success(new CapturedSecret("plain_value".ToCharArray(), req.AvailableProviders[0]));
        });

        var options = new CaptureSecretOptions
        {
            ForcedProvider = "literal",
            InputProvider = mockInput
        };

        var extensions = (IConfigurationSecretWriter)client;
        var result = await extensions.CaptureSecretAsync("app", "plain_key", options, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedReq);
        Assert.Single(capturedReq.AvailableProviders);
        Assert.Equal("literal", capturedReq.AvailableProviders[0]);
        Assert.Equal("literal", result.Value.Provider);

        var rawJson = await File.ReadAllTextAsync(Path.Combine(directory.Path, "app.json"), TestContext.Current.CancellationToken);
        Assert.Contains("plain_value", rawJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureSecretAsync_WhenUnforced_OffersAvailableProvidersAndAllowsLiteralSelection()
    {
        using var directory = new TemporaryDirectory();
        var documents = new ConfigurationDocumentFactoryLocalFile(directory.Path);
        var cipher = ConfigurationCipherAesGcm.Create(new byte[32]).Value;
        var localEncryption = new ConfigurationProviderLocalEncryption(cipher);
        var store = ConfigurationStore.Create(documents, [localEncryption]).Value;
        using var client = new ConfigurationClient(store);

        SecretPromptRequest? capturedReq = null;
        var mockInput = new MockSecretInputProvider(req =>
        {
            capturedReq = req;
            return Result<CapturedSecret>.Success(new CapturedSecret("unforced_plain_val".ToCharArray(), "literal"));
        });

        var options = new CaptureSecretOptions
        {
            InputProvider = mockInput
        };

        var extensions = (IConfigurationSecretWriter)client;
        var result = await extensions.CaptureSecretAsync("app", "unforced_key", options, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedReq);
        Assert.Contains("aesLocal", capturedReq.AvailableProviders);
        Assert.Contains("literal", capturedReq.AvailableProviders);
        Assert.Equal("aesLocal", capturedReq.PreferredProvider);
        Assert.Equal("literal", result.Value.Provider);

        var rawJson = await File.ReadAllTextAsync(Path.Combine(directory.Path, "app.json"), TestContext.Current.CancellationToken);
        Assert.Contains("unforced_plain_val", rawJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CliSecretInputProvider_DoubleEntryMismatch_ReturnsValidationError()
    {
        var input = new StringReader("password123\npassword999\n");
        var output = new StringWriter();
        var provider = new CliSecretInputProvider(input, output);

        var request = new SecretPromptRequest(
            "db",
            "password",
            "Enter password",
            ["aesLocal"],
            "aesLocal",
            confirm: true,
            timeout: TimeSpan.FromSeconds(5));

        var result = await provider.CaptureAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Validation, result.Errors[0].Category);
        Assert.Contains("match", result.Errors[0].TechnicalMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CliSecretInputProvider_MatchingConfirmation_CapturesSecret()
    {
        var input = new StringReader("1\nmySecretPass\nmySecretPass\n");
        var output = new StringWriter();
        var provider = new CliSecretInputProvider(input, output);

        var request = new SecretPromptRequest(
            "db",
            "password",
            "Enter password",
            ["aesLocal", "bws"],
            "aesLocal",
            confirm: true,
            timeout: TimeSpan.FromSeconds(5));

        var result = await provider.CaptureAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        using var captured = result.Value;
        Assert.Equal("mySecretPass", captured.ValueString);
        Assert.Equal("aesLocal", captured.SelectedProvider);
    }

    [Fact]
    public async Task LoopbackSecretInputProvider_ValidPost_CapturesSecretAndShutsDown()
    {
        using var provider = new LoopbackSecretInputProvider();
        var request = new SecretPromptRequest(
            "app",
            "apiKey",
            "Enter API Key",
            ["aesLocal"],
            "aesLocal",
            confirm: true,
            timeout: TimeSpan.FromSeconds(10));

        var captureTask = provider.CaptureAsync(request, TestContext.Current.CancellationToken);

        // Wait until listener has bound and token is available
        var url = await provider.WaitForUrlAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(url);
        Assert.Equal("127.0.0.1", url.Host);

        // Perform HTTP POST with valid token
        using var httpClient = new HttpClient();
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("session_token", provider.SessionToken),
            new KeyValuePair<string, string>("secret", "superSecretKey"),
            new KeyValuePair<string, string>("confirm_secret", "superSecretKey"),
            new KeyValuePair<string, string>("provider", "aesLocal")
        ]);

        var response = await httpClient.PostAsync(url, form, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await captureTask;
        Assert.True(result.IsSuccess);
        using var captured = result.Value;
        Assert.Equal("superSecretKey", captured.ValueString);
        Assert.Equal("aesLocal", captured.SelectedProvider);
    }

    [Fact]
    public async Task LoopbackSecretInputProvider_InvalidToken_Returns403()
    {
        using var provider = new LoopbackSecretInputProvider();
        var request = new SecretPromptRequest(
            "app",
            "apiKey",
            "Enter API Key",
            ["aesLocal"],
            "aesLocal",
            confirm: false,
            timeout: TimeSpan.FromSeconds(10));

        var captureTask = provider.CaptureAsync(request, TestContext.Current.CancellationToken);
        var url = await provider.WaitForUrlAsync(TestContext.Current.CancellationToken);

        using var httpClient = new HttpClient();
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("session_token", "invalid-token"),
            new KeyValuePair<string, string>("secret", "superSecretKey"),
            new KeyValuePair<string, string>("provider", "aesLocal")
        ]);

        var response = await httpClient.PostAsync(url, form, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        provider.Cancel();
        var result = await captureTask;
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task OAuthAuthorizationClientLoopback_OutputsUrlToConsole_AndContinuesIfBrowserFails()
    {
        var options = new OAuthOptions
        {
            AuthorizationEndpoint = new Uri("https://auth.example.test/authorize"),
            TokenEndpoint = new Uri("https://auth.example.test/token"),
            ClientId = "test-client"
        };

        var consoleOut = new StringWriter();
        var failingBrowser = new FailingBrowserLauncher();
        var endpoint = new SimpleTokenEndpointStub("access-123", "refresh-456", 3600);
        using var httpClient = new HttpClient(endpoint);

        var clientResult = OAuthAuthorizationClientLoopback.Create(
            options,
            httpClient,
            failingBrowser,
            consoleOut: consoleOut);

        Assert.True(clientResult.IsSuccess);
        var authClient = clientResult.Value;

        // Run AuthorizeAsync in background
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var authTask = authClient.AuthorizeAsync(cts.Token);

        // Verify URL was printed to consoleOut
        await Task.Delay(200, TestContext.Current.CancellationToken); // allow thread to bind and print
        var outputText = consoleOut.ToString();
        Assert.Contains("https://auth.example.test/authorize", outputText, StringComparison.Ordinal);
        Assert.Contains("client_id=test-client", outputText, StringComparison.Ordinal);

        // Emulate user completing login in external browser and loopback callback being hit
        var lines = outputText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var urlLine = lines.First(l => l.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        var authUri = new Uri(urlLine);
        var queryParams = authUri.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('='))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");

        var redirectUri = new Uri(queryParams["redirect_uri"]);
        var state = queryParams["state"];

        using var callbackClient = new HttpClient();
        var callbackUrl = $"{redirectUri.GetLeftPart(UriPartial.Path)}?code=mock-auth-code&state={state}";
        var resp = await callbackClient.GetAsync(callbackUrl, cts.Token);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var authResult = await authTask;
        Assert.True(authResult.IsSuccess);
        Assert.Equal("access-123", authResult.Value.AccessToken);
        Assert.Equal("refresh-456", authResult.Value.RefreshToken);
        Assert.Contains("successful", consoleOut.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MockSecretInputProvider : ISecretInputProvider
    {
        private readonly Func<SecretPromptRequest, Result<CapturedSecret>> _handler;

        public MockSecretInputProvider(CapturedSecret captured) => _handler = _ => Result<CapturedSecret>.Success(captured);

        public MockSecretInputProvider(Func<SecretPromptRequest, Result<CapturedSecret>> handler) => _handler = handler;

        public ValueTask<Result<CapturedSecret>> CaptureAsync(
            SecretPromptRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_handler(request));
    }

    private sealed class FailingBrowserLauncher : IBrowserLauncher
    {
        public ValueTask<Result> LaunchAsync(Uri uri, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Failure(Error.FromMessages("Launch failed", "No graphical display", ErrorCategory.Unexpected)));
    }

    private sealed class SimpleTokenEndpointStub(string accessToken, string refreshToken, int expiresIn) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = $"{{\"access_token\":\"{accessToken}\",\"refresh_token\":\"{refreshToken}\",\"expires_in\":{expiresIn},\"token_type\":\"Bearer\"}}";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
