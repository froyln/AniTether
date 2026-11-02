using System.Net;
using AniTether.Api.Features.Sources;
using Xunit;

namespace AniTether.Api.Tests;

public class SourceHttpTests
{
    [Fact]
    public async Task GetAsyncReadsNoMoreThanTheConfiguredBodyLimit()
    {
        using var http = new HttpClient(new FakeHandler(_ => FakeHandler.Json("0123456789")));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://source.example.invalid/descriptor");

        var response = await SourceHttp.GetAsync(http, request, 4, CancellationToken.None);

        Assert.Equal("0123", response.Body);
        Assert.Contains(request.Headers.UserAgent, agent => agent.Product?.Name == "AniTetherBot");
    }

    [Fact]
    public async Task ConnectorRejectsLoopbackDnsAnswersBeforeOpeningASocket()
    {
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = PublicInternetConnector.ConnectAsync };
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("https://localhost/"));

        Assert.Contains("public IP", error.ToString());
    }
}
