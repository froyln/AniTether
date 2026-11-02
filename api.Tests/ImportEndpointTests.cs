using System.Net;
using System.Net.Http.Json;
using AniTether.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AniTether.Api.Tests;

/// <summary>
/// Auth-gating only — these don't hit the real AniList network (no test in this codebase
/// does; see ImportRunnerTests for the DB-level merge/scoping coverage instead). Both
/// requests are rejected by RequireAuthorization() before any AniListClient call happens.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ImportEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private string _connectionString = null!;

    public ImportEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        _connectionString = await _postgres.CreateDatabaseAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task ImportAniList_Unauthenticated_Returns401()
    {
        var client = _factory!.CreateClient();

        var response = await client.PostAsJsonAsync("/api/import/anilist", new { username = "someone" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ImportMal_Unauthenticated_Returns401()
    {
        var client = _factory!.CreateClient();
        using var content = new MultipartFormDataContent();

        var response = await client.PostAsync("/api/import/mal", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
