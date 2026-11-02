using System.Net;
using System.Net.Http.Json;
using AniTether.Api.Data;
using AniTether.Api.Features.Anime;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AniTether.Api.Tests;

/// <summary>AniList answers 429 to everything here: the anime page must still work from the cache.</summary>
[Collection(PostgresCollection.Name)]
public class AnimeDetailEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private string _connectionString = null!;

    public AnimeDetailEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        _connectionString = await _postgres.CreateDatabaseAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<AniListClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests))));
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        // Still airing, so every view tries to re-sync from AniList first.
        db.Anime.Add(new Anime { AnilistId = 1, TitleRomaji = "Airing Show", AiringStatus = "RELEASING", SyncedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task AniListRateLimited_ServesTheCachedAnime()
    {
        var client = await TestUsers.CreateAndLoginAsync(_factory!, "detail@test.local");

        var detail = await client.GetFromJsonAsync<AnimeDetailResponse>("/api/anime/1");

        Assert.Equal("Airing Show", detail!.Anime.TitleRomaji);
    }
}
