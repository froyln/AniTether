using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AniTether.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AniTether.Api.Tests;

[Collection(PostgresCollection.Name)]
public class ProfileEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private WebApplicationFactory<Program>? _factory;

    public ProfileEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        var connectionString = await _postgres.CreateDatabaseAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:Default", connectionString));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        db.Anime.Add(new Anime { AnilistId = 100, TitleRomaji = "Profile Anime", Episodes = 12 });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ProfileSharing_IsOptInAndExposesOnlyChosenListFields()
    {
        var alice = await TestUsers.CreateAndLoginAsync(_factory!, "profile-alice@test.local");
        var aliceMe = await alice.GetFromJsonAsync<JsonElement>("/api/me");
        var aliceId = aliceMe.GetProperty("id").GetString()!;
        var anonymous = _factory!.CreateClient();

        await alice.PostAsJsonAsync("/api/list", new { anilistId = 100 });
        await alice.PatchAsJsonAsync("/api/list/100", new { progress = 4, notes = "private note" });
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/profiles/{aliceId}")).StatusCode);

        var preview = await alice.GetFromJsonAsync<JsonElement>("/api/me/profile-preview");
        Assert.True(preview.GetProperty("isPreview").GetBoolean());
        Assert.False(preview.GetProperty("isPublic").GetBoolean());

        var update = await alice.PatchAsJsonAsync("/api/me/profile", new { displayName = "Alice", bio = "Anime fan", profilePublic = true });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var profileResponse = await anonymous.GetAsync($"/api/profiles/{aliceId}");
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        var profile = await profileResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Alice", profile.GetProperty("displayName").GetString());
        Assert.Equal("Anime fan", profile.GetProperty("bio").GetString());
        var entry = profile.GetProperty("entries")[0];
        Assert.Equal(4, entry.GetProperty("progress").GetInt32());
        Assert.False(entry.TryGetProperty("notes", out _));
        Assert.False(profile.TryGetProperty("email", out _));

        var png = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), 1);
        var upload = await alice.PutAsJsonAsync("/api/me/avatar", new { contentType = "image/png", data = Convert.ToBase64String(png) });
        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);
        var avatar = await anonymous.GetAsync($"/api/profiles/{aliceId}/avatar");
        Assert.Equal(HttpStatusCode.OK, avatar.StatusCode);
        Assert.Equal("image/png", avatar.Content.Headers.ContentType?.MediaType);

        await alice.PatchAsJsonAsync("/api/me/profile", new { displayName = "Alice", bio = "Anime fan", profilePublic = false });
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/profiles/{aliceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/profiles/{aliceId}/avatar")).StatusCode);
    }

    [Fact]
    public async Task Avatar_RejectsUnsupportedAndOversizedImages()
    {
        var client = await TestUsers.CreateAndLoginAsync(_factory!, "profile-avatar@test.local");
        var unsupported = await client.PutAsJsonAsync("/api/me/avatar", new { contentType = "image/svg+xml", data = Convert.ToBase64String("<svg/>"u8.ToArray()) });
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);

        var oversized = await client.PutAsJsonAsync("/api/me/avatar", new { contentType = "image/png", data = new string('A', 1_400_100) });
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
    }
}
