using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AniTether.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AniTether.Api.Tests;

[Collection(PostgresCollection.Name)]
public class SourcesEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private string _connectionString = null!;

    public SourcesEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    private WebApplicationFactory<Program>? _factory;
    private Guid _sourceId;

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
        db.Anime.Add(new Anime { AnilistId = 1, TitleRomaji = "Test Anime", Episodes = 24 });
        db.Anime.Add(new Anime { AnilistId = 2, TitleRomaji = "Other Anime", Episodes = 12 });
        var source = new Source
        {
            Id = Guid.NewGuid(),
            Name = "Test Source",
            BaseUrl = "https://example.com",
            SearchUrlTemplate = "https://example.com/search?q={title}",
            Enabled = true,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        _sourceId = source.Id;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private Task<HttpClient> SignUpAsync(string email) => TestUsers.CreateAndLoginAsync(_factory!, email);

    private Task<HttpClient> SignUpAdminAsync(string email) => TestUsers.CreateAndLoginAsync(_factory!, email, Roles.Admin);

    [Fact]
    public async Task CreateSource_Anonymous_Returns401()
    {
        var client = _factory!.CreateClient();

        var response = await client.PostAsJsonAsync("/api/sources", new { name = "X", baseUrl = "https://example.com", searchUrlTemplate = "https://example.com/search?q={title}" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateSource_NonAdmin_Returns403()
    {
        var client = await SignUpAsync("user@test.local");

        var response = await client.PostAsJsonAsync("/api/sources", new { name = "X", baseUrl = "https://example.com", searchUrlTemplate = "https://example.com/search?q={title}" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateSource_Admin_Succeeds()
    {
        var client = await SignUpAdminAsync("admin@test.local");

        var response = await client.PostAsJsonAsync("/api/sources", new { name = "X", baseUrl = "https://example.com", searchUrlTemplate = "https://example.com/search?q={title}" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateSource_TrimsNameLikeUpdate()
    {
        var client = await SignUpAdminAsync("admin-trim@test.local");
        var response = await client.PostAsJsonAsync("/api/sources", new
        {
            name = "  X  ", baseUrl = "https://example.com",
            searchUrlTemplate = "https://example.com/search?q={title}",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("X", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task CreateSource_EpisodeUrlTemplateWrongHost_Returns400()
    {
        var client = await SignUpAdminAsync("admin2@test.local");

        var response = await client.PostAsJsonAsync("/api/sources", new
        {
            name = "X",
            baseUrl = "https://example.com",
            searchUrlTemplate = "https://example.com/search?q={title}",
            episodeUrlTemplate = "https://evil.example/watch/{episode}",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSource_EpisodeUrlTemplateMissingPlaceholder_Returns400()
    {
        var client = await SignUpAdminAsync("admin3@test.local");

        var response = await client.PostAsJsonAsync("/api/sources", new
        {
            name = "X",
            baseUrl = "https://example.com",
            searchUrlTemplate = "https://example.com/search?q={title}",
            episodeUrlTemplate = "https://example.com/watch/{slug}",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSource_ValidEpisodeUrlTemplate_Succeeds()
    {
        var client = await SignUpAdminAsync("admin4@test.local");

        var response = await client.PostAsJsonAsync("/api/sources", new
        {
            name = "X",
            baseUrl = "https://example.com",
            searchUrlTemplate = "https://example.com/search?q={title}",
            episodeUrlTemplate = "https://example.com/watch/{slug}?ep={episode}",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://example.com/watch/{slug}?ep={episode}", body.GetProperty("episodeUrlTemplate").GetString());
    }

    [Fact]
    public async Task CreateSource_AnimePageUrlTemplateWrongHost_Returns400()
    {
        var client = await SignUpAdminAsync("admin8@test.local");

        var response = await client.PostAsJsonAsync("/api/sources", new
        {
            name = "X",
            baseUrl = "https://example.com",
            searchUrlTemplate = "https://example.com/search?q={title}",
            animePageUrlTemplate = "https://evil.example/anime/{slug}",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSource_AnimePageUrlTemplateMissingPlaceholder_Returns400()
    {
        var client = await SignUpAdminAsync("admin9@test.local");

        var response = await client.PostAsJsonAsync("/api/sources", new
        {
            name = "X",
            baseUrl = "https://example.com",
            searchUrlTemplate = "https://example.com/search?q={title}",
            animePageUrlTemplate = "https://example.com/anime/show",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSource_ValidAnimePageUrlTemplate_Succeeds()
    {
        var client = await SignUpAdminAsync("admin10@test.local");

        var response = await client.PostAsJsonAsync("/api/sources", new
        {
            name = "X",
            baseUrl = "https://example.com",
            searchUrlTemplate = "https://example.com/search?q={title}",
            animePageUrlTemplate = "https://example.com/anime/{slug}",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://example.com/anime/{slug}", body.GetProperty("animePageUrlTemplate").GetString());
    }

    [Fact]
    public async Task CreateSource_InvalidLanguage_Returns400()
    {
        var client = await SignUpAdminAsync("admin11@test.local");

        var response = await client.PostAsJsonAsync("/api/sources", new
        {
            name = "X",
            baseUrl = "https://example.com",
            searchUrlTemplate = "https://example.com/search?q={title}",
            language = "fr",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSource_DefaultsToEnglish()
    {
        var client = await SignUpAdminAsync("admin12@test.local");

        var response = await client.PostAsJsonAsync("/api/sources", new
        {
            name = "X",
            baseUrl = "https://example.com",
            searchUrlTemplate = "https://example.com/search?q={title}",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("en", body.GetProperty("language").GetString());
    }

    [Fact]
    public async Task UpdateSource_LanguageChange_Succeeds()
    {
        var client = await SignUpAdminAsync("admin13@test.local");

        var response = await client.PatchAsJsonAsync($"/api/sources/{_sourceId}", new { language = "es" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("es", body.GetProperty("language").GetString());
    }

    [Fact]
    public async Task UpdateSource_NewDomainWithoutItsTemplates_Returns400()
    {
        var client = await SignUpAdminAsync("admin-move1@test.local");

        // The search template stays on example.com while the base URL moves.
        var response = await client.PatchAsJsonAsync($"/api/sources/{_sourceId}", new { baseUrl = "https://moved.example" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSource_NewDomainTogetherWithTemplates_Succeeds()
    {
        var client = await SignUpAdminAsync("admin-move2@test.local");

        var response = await client.PatchAsJsonAsync($"/api/sources/{_sourceId}", new
        {
            name = "Renamed",
            baseUrl = "https://moved.example",
            searchUrlTemplate = "https://moved.example/search?q={title}",
            episodeUrlTemplate = "https://moved.example/watch/{anilist_id}/{episode}",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed", body.GetProperty("name").GetString());
        Assert.Equal("https://moved.example", body.GetProperty("baseUrl").GetString());
        Assert.Equal("https://moved.example/watch/{anilist_id}/{episode}", body.GetProperty("episodeUrlTemplate").GetString());
    }

    [Fact]
    public async Task UpdateSource_EmptyName_Returns400()
    {
        var client = await SignUpAdminAsync("admin-name@test.local");

        var response = await client.PatchAsJsonAsync($"/api/sources/{_sourceId}", new { name = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSource_InvalidLanguage_Returns400()
    {
        var client = await SignUpAdminAsync("admin14@test.local");

        var response = await client.PatchAsJsonAsync($"/api/sources/{_sourceId}", new { language = "fr" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSource_EmbedUrlTemplateWrongHost_Returns400()
    {
        var client = await SignUpAdminAsync("admin15@test.local");

        var response = await client.PostAsJsonAsync("/api/sources", new
        {
            name = "X",
            baseUrl = "https://example.com",
            searchUrlTemplate = "https://example.com/search?q={title}",
            embedUrlTemplate = "https://evil.example/embed/{episode}",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSource_EmbedUrlTemplateMissingPlaceholder_Returns400()
    {
        var client = await SignUpAdminAsync("admin16@test.local");

        var response = await client.PostAsJsonAsync("/api/sources", new
        {
            name = "X",
            baseUrl = "https://example.com",
            searchUrlTemplate = "https://example.com/search?q={title}",
            embedUrlTemplate = "https://example.com/embed/{anilist_id}",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSource_ValidEmbedUrlTemplate_DefaultsToSandboxed()
    {
        var client = await SignUpAdminAsync("admin17@test.local");

        var response = await client.PostAsJsonAsync("/api/sources", new
        {
            name = "X",
            baseUrl = "https://example.com",
            searchUrlTemplate = "https://example.com/search?q={title}",
            embedUrlTemplate = "https://example.com/embed/{anilist_id}/{episode}",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://example.com/embed/{anilist_id}/{episode}", body.GetProperty("embedUrlTemplate").GetString());
        Assert.True(body.GetProperty("sandboxed").GetBoolean());
    }

    [Fact]
    public async Task UpdateSource_SandboxedFalse_Succeeds()
    {
        var client = await SignUpAdminAsync("admin18@test.local");

        var response = await client.PatchAsJsonAsync($"/api/sources/{_sourceId}", new { sandboxed = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("sandboxed").GetBoolean());
    }

    [Fact]
    public async Task SetLink_JavascriptUrl_Returns400()
    {
        var client = await SignUpAsync("user@test.local");

        var response = await client.PostAsJsonAsync($"/api/sources/{_sourceId}/links", new { anilistId = 1, url = "javascript:alert(1)" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetLink_WrongHost_Returns400()
    {
        var client = await SignUpAsync("user@test.local");

        var response = await client.PostAsJsonAsync($"/api/sources/{_sourceId}/links", new { anilistId = 1, url = "https://evil.example/anime/frieren" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetLink_IsVisibleToOtherUsers_ButLastSourceIdIsPerUser()
    {
        var clientA = await SignUpAsync("a@test.local");
        var clientB = await SignUpAsync("b@test.local");

        await clientA.PostAsJsonAsync("/api/list", new { anilistId = 1 });
        await clientB.PostAsJsonAsync("/api/list", new { anilistId = 1 });

        var setLink = await clientA.PostAsJsonAsync($"/api/sources/{_sourceId}/links", new { anilistId = 1, url = "https://example.com/anime/frieren" });
        Assert.Equal(HttpStatusCode.OK, setLink.StatusCode);

        var bList = await clientB.GetFromJsonAsync<List<JsonElement>>("/api/list");
        var bEntry = bList!.Single(e => e.GetProperty("anilistId").GetInt32() == 1);
        var bLinks = bEntry.GetProperty("sourceLinks").EnumerateArray().ToList();
        Assert.Single(bLinks);
        Assert.Equal("https://example.com/anime/frieren", bLinks[0].GetProperty("url").GetString());
        Assert.True(bEntry.GetProperty("lastSourceId").ValueKind is JsonValueKind.Null);

        var aList = await clientA.GetFromJsonAsync<List<JsonElement>>("/api/list");
        var aEntry = aList!.Single(e => e.GetProperty("anilistId").GetInt32() == 1);
        Assert.Equal(_sourceId, aEntry.GetProperty("lastSourceId").GetGuid());
    }

    [Fact]
    public async Task SetLink_SlugAlreadyClaimedByDifferentAnime_Returns409()
    {
        var client = await SignUpAsync("user@test.local");

        var first = await client.PostAsJsonAsync($"/api/sources/{_sourceId}/links", new { anilistId = 1, url = "https://example.com/anime/frieren" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync($"/api/sources/{_sourceId}/links", new { anilistId = 2, url = "https://example.com/anime/frieren" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}
