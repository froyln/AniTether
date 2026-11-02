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
public class ListAuthorizationTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private string _connectionString = null!;

    public ListAuthorizationTests(PostgresFixture postgres) => _postgres = postgres;

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
        db.Anime.Add(new Anime { AnilistId = 1, TitleRomaji = "Test Anime", Episodes = 24 });
        await db.SaveChangesAsync();
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
    public async Task Progress_AtTotalEpisodes_CompletesEntry()
    {
        var client = await SignUpAsync("progress@test.local");

        await client.PostAsJsonAsync("/api/list", new { anilistId = 1 });
        var response = await client.PatchAsJsonAsync("/api/list/1", new { progress = 24 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task LegacyApplyProgressRequest_WithoutRevision_RemainsSupported()
    {
        var client = await SignUpAsync("legacy-progress@test.local");
        await client.PostAsJsonAsync("/api/list", new { anilistId = 1 });

        var response = await client.PostAsJsonAsync("/api/list/1/apply-progress", new
        {
            episode = 1,
            lastSourceId = (Guid?)null,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("progress").GetInt32());
    }

    [Fact]
    public async Task StaleAutomaticProgress_DoesNotUndoManualCorrection()
    {
        var client = await SignUpAsync("stale-progress@test.local");
        await client.PostAsJsonAsync("/api/list", new { anilistId = 1 });
        await client.PatchAsJsonAsync("/api/list/1", new { progress = 5 });
        var watchedEntry = await client.GetFromJsonAsync<JsonElement>("/api/list/1");
        var observedRevision = watchedEntry.GetProperty("revision").GetString();

        var correction = await client.PatchAsJsonAsync("/api/list/1", new { progress = 2 });
        Assert.Equal(HttpStatusCode.OK, correction.StatusCode);

        var staleUpdate = await client.PostAsJsonAsync("/api/list/1/apply-progress", new
        {
            episode = 4,
            lastSourceId = (Guid?)null,
            observedRevision,
        });

        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        var currentEntry = await client.GetFromJsonAsync<JsonElement>("/api/list/1");
        Assert.Equal(2, currentEntry.GetProperty("progress").GetInt32());
    }

    [Fact]
    public async Task ReplayedSyncEvent_ReturnsOriginalAcceptanceWithoutRepeatingWrite()
    {
        var client = await SignUpAsync("sync-replay@test.local");
        var eventId = Guid.NewGuid();
        var batch = new
        {
            contractVersion = 1,
            events = new[] { new { eventId, type = "listEntry.add", anilistId = 1, baseRevision = "0" } },
        };

        var firstResponse = await client.PostAsJsonAsync("/api/me/sync/events", batch);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var replayResponse = await client.PostAsJsonAsync("/api/me/sync/events", batch);
        var replay = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var firstResult = first.GetProperty("results")[0];
        var replayResult = replay.GetProperty("results")[0];
        Assert.Equal("accepted", firstResult.GetProperty("status").GetString());
        Assert.Equal(firstResult.GetProperty("cursor").GetInt64(), replayResult.GetProperty("cursor").GetInt64());

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.ListEntries.CountAsync());
        Assert.Equal(1, await db.SyncChanges.CountAsync());
    }

    [Fact]
    public async Task DelayedEditFromSecondSessionConflictsWithoutOverwritingCurrentEntry()
    {
        using var client = await SignUpAsync("out-of-order@test.local");
        using var secondSession = await TestUsers.LoginAsync(_factory!, "out-of-order@test.local");
        var created = await client.PostAsJsonAsync("/api/list", new { anilistId = 1 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var newest = await client.PostAsJsonAsync("/api/me/sync/events", new
        {
            contractVersion = 1,
            events = new[]
            {
                new { eventId = Guid.NewGuid(), type = "listEntry.patch", anilistId = 1, baseRevision = "1", progress = 3 },
            },
        });
        Assert.Equal(HttpStatusCode.OK, newest.StatusCode);
        using var newestBody = JsonDocument.Parse(await newest.Content.ReadAsStringAsync());
        Assert.Equal("accepted", newestBody.RootElement.GetProperty("results")[0].GetProperty("status").GetString());

        var delayed = await secondSession.PostAsJsonAsync("/api/me/sync/events", new
        {
            contractVersion = 1,
            events = new[]
            {
                new { eventId = Guid.NewGuid(), type = "listEntry.patch", anilistId = 1, baseRevision = "1", score = 10 },
            },
        });
        Assert.Equal(HttpStatusCode.OK, delayed.StatusCode);
        using var delayedBody = JsonDocument.Parse(await delayed.Content.ReadAsStringAsync());
        var delayedResult = delayedBody.RootElement.GetProperty("results")[0];
        Assert.Equal("conflict", delayedResult.GetProperty("status").GetString());
        Assert.Equal("revision_mismatch", delayedResult.GetProperty("code").GetString());

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.ListEntries.SingleAsync(row => row.AnilistId == 1);
        Assert.Equal(2L, entry.Revision);
        Assert.Equal(3, entry.Progress);
        Assert.Null(entry.Score);
        Assert.Equal(2, await db.SyncChanges.CountAsync(row => row.EntityType == "listEntry"));
    }

    [Fact]
    public async Task Status_AsAnInteger_IsRejected()
    {
        var client = await SignUpAsync("int-status@test.local");
        await client.PostAsJsonAsync("/api/list", new { anilistId = 1 });

        var response = await client.PatchAsJsonAsync("/api/list/1", new { status = 99 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UniqueViolation_IsRecognized()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Anime.Add(new Anime { AnilistId = 1, TitleRomaji = "Duplicate" });

        var e = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(AppDbContext.IsUniqueViolation(e));
    }

    [Fact]
    public async Task OtherUsers_CannotSeeOrEditEachOthersEntries()
    {
        var clientA = await SignUpAsync("a@test.local");
        var clientB = await SignUpAsync("b@test.local");

        var create = await clientA.PostAsJsonAsync("/api/list", new { anilistId = 1 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var bList = await clientB.GetFromJsonAsync<List<JsonElement>>("/api/list");
        Assert.Empty(bList!);

        var bEntry = await clientB.GetAsync("/api/list/1");
        Assert.Equal(HttpStatusCode.NotFound, bEntry.StatusCode);

        var patch = await clientB.PatchAsJsonAsync("/api/list/1", new { progress = 1 });
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);

        var delete = await clientB.DeleteAsync("/api/list/1");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        var aList = await clientA.GetFromJsonAsync<List<JsonElement>>("/api/list");
        Assert.Single(aList!);

        var aEntry = await clientA.GetFromJsonAsync<JsonElement>("/api/list/1");
        Assert.Equal(1, aEntry.GetProperty("anilistId").GetInt32());
    }
}
