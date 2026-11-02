using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AniTether.Api.Tests;

[Collection(PostgresCollection.Name)]
public class SecurityHardeningTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private string _connectionString = null!;

    public SecurityHardeningTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync() => _connectionString = await _postgres.CreateDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>TestServer has no TCP peer; pretend the request came from <paramref name="peer"/>
    /// (a reverse proxy, or a client hitting the app directly).</summary>
    private sealed class PeerAddress(string peer) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private async Task<WebApplicationFactory<Program>> FactoryAsync(string peer)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseSetting("ForwardedHeaders:KnownNetworks", "10.0.0.0/8");
            builder.ConfigureServices(s => s.AddTransient<IStartupFilter>(_ => new PeerAddress(peer)));
        });

        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Data.AppDbContext>().Database.MigrateAsync();
        return factory;
    }

    private static Task<HttpResponseMessage> FailedLogin(HttpClient client, string clientIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "nobody@test.local", password = "wrong" }),
        };
        request.Headers.Add("X-Forwarded-For", clientIp);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task BehindTrustedProxy_EachClientIpGetsItsOwnRateLimitBucket()
    {
        await using var factory = await FactoryAsync("10.0.0.5");
        var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            await FailedLogin(client, "198.51.100.1");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await FailedLogin(client, "198.51.100.1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await FailedLogin(client, "198.51.100.2")).StatusCode);
    }

    [Fact]
    public async Task FromUntrustedPeer_ForwardedHeaderIsIgnored()
    {
        await using var factory = await FactoryAsync("203.0.113.9");
        var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            await FailedLogin(client, $"198.51.100.{i + 1}");
        }

        // Spoofing a new X-Forwarded-For per request doesn't buy a fresh bucket.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await FailedLogin(client, "198.51.100.99")).StatusCode);
    }

    [Fact]
    public async Task Responses_CarrySecurityHeaders()
    {
        await using var factory = await FactoryAsync("10.0.0.5");

        var response = await factory.CreateClient().GetAsync("/api/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task SearchQuery_OverLimit_Returns400()
    {
        await using var factory = await FactoryAsync("10.0.0.5");
        var client = await TestUsers.CreateAndLoginAsync(factory, "search@test.local");

        var response = await client.GetAsync($"/api/anime/search?q={new string('a', 101)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InProduction_UnhandledError_IsProblemDetailsWithoutStackTrace()
    {
        // No DB behind this connection string: the login handler throws.
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Default", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1");
            builder.UseSetting("HealthChecks:Enabled", "false");
        });

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "a@test.local", password = "x" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("   at ", body);
        Assert.DoesNotContain("Npgsql", body);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }
}
