using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AniTether.Api.Tests;

[Collection(PostgresCollection.Name)]
public class AuthPreferencesEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private string _connectionString = null!;

    public AuthPreferencesEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        _connectionString = await _postgres.CreateDatabaseAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        await db.Database.MigrateAsync();
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
    public async Task UpdatePreferences_ValidLanguage_ReflectedInMe()
    {
        var client = await SignUpAsync("prefs1@test.local");

        var patch = await client.PatchAsJsonAsync("/api/me/preferences", new { preferredLanguage = "es" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("es", me.GetProperty("preferredLanguage").GetString());
    }

    [Fact]
    public async Task UpdatePreferences_InvalidLanguage_Returns400()
    {
        var client = await SignUpAsync("prefs2@test.local");

        var patch = await client.PatchAsJsonAsync("/api/me/preferences", new { preferredLanguage = "fr" });

        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    [Fact]
    public async Task UpdatePreferences_NullClearsPreference()
    {
        var client = await SignUpAsync("prefs3@test.local");
        await client.PatchAsJsonAsync("/api/me/preferences", new { preferredLanguage = "es" });

        var patch = await client.PatchAsJsonAsync("/api/me/preferences", new { preferredLanguage = (string?)null });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(JsonValueKind.Null, me.GetProperty("preferredLanguage").ValueKind);
    }

    [Fact]
    public async Task UpdatePreferences_ScopedToCaller_DoesNotAffectOtherUser()
    {
        var alice = await SignUpAsync("prefs-alice@test.local");
        var bob = await SignUpAsync("prefs-bob@test.local");

        await alice.PatchAsJsonAsync("/api/me/preferences", new { preferredLanguage = "es" });
        await bob.PatchAsJsonAsync("/api/me/preferences", new { preferredLanguage = "en" });

        var aliceMe = await alice.GetFromJsonAsync<JsonElement>("/api/me");
        var bobMe = await bob.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("es", aliceMe.GetProperty("preferredLanguage").GetString());
        Assert.Equal("en", bobMe.GetProperty("preferredLanguage").GetString());
    }

    [Fact]
    public async Task UpdatePreferences_Unauthenticated_Returns401()
    {
        var client = _factory!.CreateClient();

        var patch = await client.PatchAsJsonAsync("/api/me/preferences", new { preferredLanguage = "es" });

        Assert.Equal(HttpStatusCode.Unauthorized, patch.StatusCode);
    }

    [Fact]
    public async Task UpdatePreferences_Audio_ReflectedInMe_AndClearsOnNull()
    {
        var client = await SignUpAsync("prefs-audio@test.local");

        var patch = await client.PatchAsJsonAsync("/api/me/preferences", new { preferredAudio = "dub" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("dub", me.GetProperty("preferredAudio").GetString());

        var clear = await client.PatchAsJsonAsync("/api/me/preferences", new { preferredAudio = (string?)null });
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(JsonValueKind.Null, me.GetProperty("preferredAudio").ValueKind);
    }

    [Fact]
    public async Task UpdatePreferences_InvalidAudio_Returns400()
    {
        var client = await SignUpAsync("prefs-audio-bad@test.local");

        var patch = await client.PatchAsJsonAsync("/api/me/preferences", new { preferredAudio = "raw" });

        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    [Fact]
    public async Task UpdatePreferences_Audio_ScopedToCaller()
    {
        var alice = await SignUpAsync("prefs-alice2@test.local");
        var bob = await SignUpAsync("prefs-bob2@test.local");

        await alice.PatchAsJsonAsync("/api/me/preferences", new { preferredAudio = "dub" });
        await bob.PatchAsJsonAsync("/api/me/preferences", new { preferredAudio = "sub" });

        var aliceMe = await alice.GetFromJsonAsync<JsonElement>("/api/me");
        var bobMe = await bob.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("dub", aliceMe.GetProperty("preferredAudio").GetString());
        Assert.Equal("sub", bobMe.GetProperty("preferredAudio").GetString());
    }

    [Fact]
    public async Task UpdatePreferences_LanguageAndAudio_SetTogether_WithoutLosingEither()
    {
        var client = await SignUpAsync("prefs-both@test.local");

        await client.PatchAsJsonAsync("/api/me/preferences", new { preferredLanguage = "es", preferredAudio = "dub" });
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("es", me.GetProperty("preferredLanguage").GetString());
        Assert.Equal("dub", me.GetProperty("preferredAudio").GetString());
    }

    [Fact]
    public async Task UpdatePreferences_Theme_ReflectedInMe()
    {
        var client = await SignUpAsync("theme1@test.local");

        var patch = await client.PatchAsJsonAsync("/api/me/preferences", new { theme = "crimson", colorMode = "light" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("crimson", me.GetProperty("theme").GetString());
        Assert.Equal("light", me.GetProperty("colorMode").GetString());
    }

    [Theory]
    [InlineData("purple", null)]
    [InlineData(null, "sepia")]
    public async Task UpdatePreferences_UnknownThemeOrMode_Returns400(string? theme, string? colorMode)
    {
        var client = await SignUpAsync("theme2@test.local");

        var patch = await client.PatchAsJsonAsync("/api/me/preferences", new { theme, colorMode });

        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }
}
