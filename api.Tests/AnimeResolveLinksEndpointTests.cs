using System.Net.Http.Json;
using AniTether.Api.Data;
using AniTether.Api.Features.Anime;
using AniTether.Api.Features.Sources;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AniTether.Api.Tests;

[Collection(PostgresCollection.Name)]
public class AnimeResolveLinksEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private string _connectionString = null!;

    public AnimeResolveLinksEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    private WebApplicationFactory<Program>? _factory;
    private Guid _matchingSourceId;
    private Guid _nonMatchingSourceId;
    private Guid _insecureSourceId;
    private Guid _splitPathSourceId;
    private Guid _downSourceId;

    public async Task InitializeAsync()
    {
        _connectionString = await _postgres.CreateDatabaseAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<SourceLinkResolver>()
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeHandler(request =>
                    {
                        if (request.RequestUri!.Host == "down.example")
                        {
                            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
                        }

                        var html = request.RequestUri!.Host switch
                        {
                            "matching.example" => """<a href="/anime/test-anime/1">Test Anime</a>""",
                            // Same-host but http:// — a matched href must not bypass the https-only storage guard.
                            "insecure.example" => """<a href="http://insecure.example/anime/test-anime/1">Test Anime</a>""",
                            // Anime page and episode player live under different paths — only
                            // matchable via AnimePageUrlTemplate, and {slug} is not the last
                            // path segment, so a wrong slug-derivation fallback would show up.
                            "splitpath.example" => """<a href="/anime/test-anime/info">Test Anime</a>""",
                            _ => "<p>no results</p>",
                        };
                        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html"),
                        };
                    }));
            });
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        db.Anime.Add(new Anime { AnilistId = 1, TitleRomaji = "Test Anime", Episodes = 24 });

        var matching = new Source
        {
            Id = Guid.NewGuid(),
            Name = "Matching Source",
            BaseUrl = "https://matching.example",
            SearchUrlTemplate = "https://matching.example/search?q={title}",
            EpisodeUrlTemplate = "https://matching.example/anime/{slug}/{episode}",
            Enabled = true,
        };
        var nonMatching = new Source
        {
            Id = Guid.NewGuid(),
            Name = "Non-matching Source",
            BaseUrl = "https://nomatch.example",
            SearchUrlTemplate = "https://nomatch.example/search?q={title}",
            EpisodeUrlTemplate = "https://nomatch.example/anime/{slug}/{episode}",
            Enabled = true,
        };
        var insecure = new Source
        {
            Id = Guid.NewGuid(),
            Name = "Insecure Source",
            BaseUrl = "https://insecure.example",
            SearchUrlTemplate = "https://insecure.example/search?q={title}",
            EpisodeUrlTemplate = "https://insecure.example/anime/{slug}/{episode}",
            Enabled = true,
        };
        var splitPath = new Source
        {
            Id = Guid.NewGuid(),
            Name = "Split-path Source",
            BaseUrl = "https://splitpath.example",
            SearchUrlTemplate = "https://splitpath.example/search?q={title}",
            EpisodeUrlTemplate = "https://splitpath.example/watch/{slug}/{episode}",
            AnimePageUrlTemplate = "https://splitpath.example/anime/{slug}/info",
            Enabled = true,
        };
        db.Sources.Add(matching);
        db.Sources.Add(nonMatching);
        db.Sources.Add(insecure);
        var down = new Source
        {
            Id = Guid.NewGuid(),
            Name = "Down Source",
            BaseUrl = "https://down.example",
            SearchUrlTemplate = "https://down.example/search?q={title}",
            EpisodeUrlTemplate = "https://down.example/anime/{slug}/{episode}",
            Enabled = true,
        };
        db.Sources.Add(splitPath);
        db.Sources.Add(down);
        await db.SaveChangesAsync();
        _matchingSourceId = matching.Id;
        _nonMatchingSourceId = nonMatching.Id;
        _insecureSourceId = insecure.Id;
        _splitPathSourceId = splitPath.Id;
        _downSourceId = down.Id;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private Task<HttpClient> SignUpAsync(string email) => TestUsers.CreateAndLoginAsync(_factory!, email);

    [Fact]
    public async Task ResolveLinks_CreatesLinkForMatchingSource_AndRecordsMissForOther()
    {
        var client = await SignUpAsync("resolver@test.local");

        var response = await client.PostAsync("/api/anime/1/resolve-links", null);
        response.EnsureSuccessStatusCode();

        var detail = await response.Content.ReadFromJsonAsync<AnimeDetailResponse>();
        Assert.Contains(detail!.SourceLinks, l => l.SourceId == _matchingSourceId && l.Slug == "test-anime");
        Assert.DoesNotContain(detail.SourceLinks, l => l.SourceId == _nonMatchingSourceId);

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var missRow = await db.SourceLinkLookups.FindAsync(1, _nonMatchingSourceId);
        Assert.NotNull(missRow);
        Assert.True(missRow!.CheckedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task ResolveLinks_HttpMatch_NotStored()
    {
        var client = await SignUpAsync("resolver-insecure@test.local");

        var response = await client.PostAsync("/api/anime/1/resolve-links", null);
        response.EnsureSuccessStatusCode();

        var detail = await response.Content.ReadFromJsonAsync<AnimeDetailResponse>();
        Assert.DoesNotContain(detail!.SourceLinks, l => l.SourceId == _insecureSourceId);

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var missRow = await db.SourceLinkLookups.FindAsync(1, _insecureSourceId);
        Assert.NotNull(missRow);
    }

    [Fact]
    public async Task ResolveLinks_AnimePageTemplate_DerivesSlugFromAnimePageNotEpisodeTemplate()
    {
        // Regression for a real bug caught in review: matching goes through
        // AnimePageUrlTemplate, but the stored slug used to always be derived against
        // EpisodeUrlTemplate. That only worked when {slug} was the URL's last segment.
        // Here {slug} is a middle
        // segment ("/anime/{slug}/info") under a completely different path than the
        // episode template ("/watch/{slug}/{episode}") - deriving against the wrong
        // template would store "info" (the last segment) instead of "test-anime".
        var client = await SignUpAsync("resolver-splitpath@test.local");

        var response = await client.PostAsync("/api/anime/1/resolve-links", null);
        response.EnsureSuccessStatusCode();

        var detail = await response.Content.ReadFromJsonAsync<AnimeDetailResponse>();
        Assert.Contains(detail!.SourceLinks, l => l.SourceId == _splitPathSourceId && l.Slug == "test-anime");
    }

    [Fact]
    public async Task ResolveLinks_UnknownAnime_Returns404()
    {
        var client = await SignUpAsync("resolver2@test.local");

        var response = await client.PostAsync("/api/anime/999/resolve-links", null);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ResolveLinks_SourceDown_RecordsNoMiss()
    {
        var client = await SignUpAsync("resolver-down@test.local");

        (await client.PostAsync("/api/anime/1/resolve-links", null)).EnsureSuccessStatusCode();

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(await db.SourceLinkLookups.FindAsync(1, _downSourceId));
        Assert.NotNull(await db.SourceLinkLookups.FindAsync(1, _nonMatchingSourceId));
    }

    [Fact]
    public async Task ResolveLinks_SecondCall_DoesNotRetryRecentMiss()
    {
        var client = await SignUpAsync("resolver3@test.local");
        await (await client.PostAsync("/api/anime/1/resolve-links", null)).Content.ReadAsStringAsync();

        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var missRow = await db.SourceLinkLookups.FindAsync(1, _nonMatchingSourceId);
            var firstCheckedAt = missRow!.CheckedAt;

            var second = await client.PostAsync("/api/anime/1/resolve-links", null);
            second.EnsureSuccessStatusCode();

            await db.Entry(missRow).ReloadAsync();
            Assert.Equal(firstCheckedAt, missRow.CheckedAt);
        }
    }
}
