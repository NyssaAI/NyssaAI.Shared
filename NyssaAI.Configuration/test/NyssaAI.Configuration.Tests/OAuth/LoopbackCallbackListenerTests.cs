using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using NyssaAI.Configuration.OAuth.Infrastructure;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.OAuth;

/// <summary>
/// The listener that receives the authorization callback. The test plays the browser over a real
/// loopback socket, because the properties worth protecting here -- that the port is loopback-only,
/// that a stray request does not end the wait, that the port is always released -- are all invisible
/// to a sign-in that simply succeeds.
/// </summary>
public sealed class LoopbackCallbackListenerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void Bind_IsLoopbackOnly_NeverAWildcardAddress()
    {
        using var listener = LoopbackCallbackListener.Create().Value;

        // Asserting on RedirectUri would prove nothing: it is built from a hardcoded 127.0.0.1
        // string and stays loopback even if the socket binds 0.0.0.0. The bound endpoint is the
        // only thing that answers the question, and a wildcard bind exposes the callback to the
        // whole network while changing nothing a local sign-in can observe.
        var bound = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Where(endpoint => endpoint.Port == listener.Port)
            .ToArray();

        Assert.NotEmpty(bound);
        Assert.All(bound, endpoint => Assert.True(
            IPAddress.IsLoopback(endpoint.Address),
            $"port {listener.Port} is bound to {endpoint.Address}, which is not loopback"));
    }

    [Fact]
    public void RedirectUri_PointsAtTheBoundPortAndCallbackPath()
    {
        using var listener = LoopbackCallbackListener.Create().Value;

        Assert.Equal("/callback", listener.RedirectUri.AbsolutePath);
        Assert.Equal(listener.Port, listener.RedirectUri.Port);
        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(listener.RedirectUri.Host.Trim('[', ']'))));
    }

    [Fact]
    public async Task WaitForCallback_WithTheCallback_ReturnsItsQueryValues()
    {
        using var cancellation = new CancellationTokenSource(Patience);
        var listener = LoopbackCallbackListener.Create().Value;
        var waiting = listener.WaitForCallbackAsync(cancellation.Token).AsTask();

        var status = await SendAsync(listener, "GET /callback?code=abc123&state=xyz789 HTTP/1.1");

        Assert.Contains("200 OK", status, StringComparison.Ordinal);
        var query = await waiting;
        Assert.True(query.IsSuccess, query.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.Equal("abc123", query.Value["code"]);
        Assert.Equal("xyz789", query.Value["state"]);
    }

    [Fact]
    public async Task WaitForCallback_DecodesPercentEncodingAndPluses()
    {
        using var cancellation = new CancellationTokenSource(Patience);
        var listener = LoopbackCallbackListener.Create().Value;
        var waiting = listener.WaitForCallbackAsync(cancellation.Token).AsTask();

        await SendAsync(listener, "GET /callback?error=access_denied&error_description=User+said+no%21 HTTP/1.1");

        var query = await waiting;
        Assert.True(query.IsSuccess);
        // The listener reports the callback as it arrived; refusing an error response is the
        // authorization client's decision, not this one's.
        Assert.Equal("access_denied", query.Value["error"]);
        Assert.Equal("User said no!", query.Value["error_description"]);
    }

    [Fact]
    public async Task WaitForCallback_WithARequestForAnotherPath_AnswersAndKeepsWaiting()
    {
        using var cancellation = new CancellationTokenSource(Patience);
        var listener = LoopbackCallbackListener.Create().Value;
        var waiting = listener.WaitForCallbackAsync(cancellation.Token).AsTask();

        // A browser prefetch or a favicon request must not consume the one callback the flow gets.
        var noise = await SendAsync(listener, "GET /favicon.ico HTTP/1.1");
        Assert.Contains("404", noise, StringComparison.Ordinal);
        Assert.False(waiting.IsCompleted);

        await SendAsync(listener, "GET /callback?code=late&state=s HTTP/1.1");

        var query = await waiting;
        Assert.True(query.IsSuccess);
        Assert.Equal("late", query.Value["code"]);
    }

    [Fact]
    public async Task WaitForCallback_WithANonGetRequest_AnswersAndKeepsWaiting()
    {
        using var cancellation = new CancellationTokenSource(Patience);
        var listener = LoopbackCallbackListener.Create().Value;
        var waiting = listener.WaitForCallbackAsync(cancellation.Token).AsTask();

        var refused = await SendAsync(listener, "POST /callback HTTP/1.1");
        Assert.Contains("405", refused, StringComparison.Ordinal);
        Assert.False(waiting.IsCompleted);

        await SendAsync(listener, "GET /callback?code=only-get&state=s HTTP/1.1");

        var query = await waiting;
        Assert.True(query.IsSuccess);
        Assert.Equal("only-get", query.Value["code"]);
    }

    [Fact]
    public async Task WaitForCallback_MatchesTheCallbackPathExactly()
    {
        using var cancellation = new CancellationTokenSource(Patience);
        var listener = LoopbackCallbackListener.Create().Value;
        var waiting = listener.WaitForCallbackAsync(cancellation.Token).AsTask();

        // A prefix or case-insensitive match would widen the surface that counts as the callback.
        Assert.Contains("404", await SendAsync(listener, "GET /callback/extra?code=a HTTP/1.1"), StringComparison.Ordinal);
        Assert.Contains("404", await SendAsync(listener, "GET /CALLBACK?code=a HTTP/1.1"), StringComparison.Ordinal);
        Assert.False(waiting.IsCompleted);

        await SendAsync(listener, "GET /callback?code=exact&state=s HTTP/1.1");
        Assert.Equal("exact", (await waiting).Value["code"]);
    }

    [Fact]
    public async Task WaitForCallback_OnCancellation_FailsAndReleasesThePort()
    {
        using var cancellation = new CancellationTokenSource();
        var listener = LoopbackCallbackListener.Create().Value;
        var port = listener.Port;
        var waiting = listener.WaitForCallbackAsync(cancellation.Token).AsTask();

        await cancellation.CancelAsync();
        var result = await waiting;

        Assert.True(result.IsFailure);
        // Rebinding the same port is the proof the finally actually released it; a leaked listener
        // would make the next sign-in fail on a port nothing appears to be using.
        var rebound = LoopbackCallbackListener.Create(port);
        Assert.True(rebound.IsSuccess, rebound.Errors.FirstOrDefault()?.TechnicalMessage);
        rebound.Value.Dispose();
    }

    [Fact]
    public async Task WaitForCallback_AfterTheCallback_ReleasesThePort()
    {
        using var cancellation = new CancellationTokenSource(Patience);
        var listener = LoopbackCallbackListener.Create().Value;
        var port = listener.Port;
        var waiting = listener.WaitForCallbackAsync(cancellation.Token).AsTask();
        await SendAsync(listener, "GET /callback?code=done&state=s HTTP/1.1");
        Assert.True((await waiting).IsSuccess);

        var rebound = LoopbackCallbackListener.Create(port);

        Assert.True(rebound.IsSuccess, rebound.Errors.FirstOrDefault()?.TechnicalMessage);
        rebound.Value.Dispose();
    }

    [Fact]
    public async Task WaitForCallback_AfterDispose_Refuses()
    {
        var listener = LoopbackCallbackListener.Create().Value;
        listener.Dispose();
        listener.Dispose();

        var result = await listener.WaitForCallbackAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(LoopbackCallbackListener.Closed, Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData("")]
    [InlineData("callback")]
    [InlineData("http://example.test/callback")]
    public void Create_WithACallbackPathThatIsNotRooted_Fails(string callbackPath)
    {
        var created = LoopbackCallbackListener.Create(callbackPath: callbackPath);

        Assert.True(created.IsFailure);
        Assert.Equal(LoopbackCallbackListener.CallbackPathInvalid(callbackPath), Assert.Single(created.Errors));
    }

    [Fact]
    public void Create_TwiceOnOnePort_RefusesTheSecond()
    {
        using var first = LoopbackCallbackListener.Create().Value;

        // Another process must not be able to sit on the callback port and receive the code.
        var second = LoopbackCallbackListener.Create(first.Port);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorCategory.Unexpected, Assert.Single(second.Errors).Category);
    }

    // Plays the browser: opens a connection to the listener and sends one raw request line.
    private static async Task<string> SendAsync(LoopbackCallbackListener listener, string requestLine)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listener.Port, TestContext.Current.CancellationToken);

        await using var stream = client.GetStream();
        var request = Encoding.ASCII.GetBytes($"{requestLine}\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(request, TestContext.Current.CancellationToken);
        await stream.FlushAsync(TestContext.Current.CancellationToken);

        using var response = new MemoryStream();
        await stream.CopyToAsync(response, TestContext.Current.CancellationToken);
        return Encoding.UTF8.GetString(response.ToArray());
    }
}
