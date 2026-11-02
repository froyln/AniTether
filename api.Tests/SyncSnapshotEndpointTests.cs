using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AniTether.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AniTether.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SyncSnapshotEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private WebApplicationFactory<Program>? _factory;

    public SyncSnapshotEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        var connectionString = await _postgres.CreateDatabaseAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Default", connectionString));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        db.Anime.AddRange(Enumerable.Range(1, 3).Select(id => new Anime
        {
            AnilistId = id,
            TitleRomaji = $"Anime {id}",
            Episodes = 24,
            AiringStatus = "FINISHED",
            SyncedAt = DateTimeOffset.UtcNow,
        }));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
    }

    [Fact]
    public async Task SnapshotPagesEntriesAndResumePositionsThenDeltaIncludesTombstone()
    {
        using var client = await TestUsers.CreateAndLoginAsync(_factory!, "sync-snapshot@test.local");
        foreach (var id in new[] { 1, 2, 3 })
        {
            var created = await client.PostAsJsonAsync("/api/list", new { anilistId = id });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        foreach (var episode in new[] { 1, 2 })
        {
            var response = await client.PostAsJsonAsync("/api/me/sync/events", new
            {
                contractVersion = 1,
                events = new[]
                {
                    new
                    {
                        eventId = Guid.NewGuid(),
                        type = "resume.start",
                        anilistId = 1,
                        baseRevision = "0",
                        episode,
                        sessionId = Guid.NewGuid(),
                        positionMs = 1_000L * episode,
                        durationMs = 100_000L,
                    },
                },
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var eventBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("accepted", eventBody.RootElement.GetProperty("results")[0].GetProperty("status").GetString());
        }

        var snapshot = await GetSnapshotAsync(client, "/api/me/sync?limit=1");
        var snapshotCursor = snapshot.GetProperty("snapshotCursor").GetString();
        Assert.Equal(1, snapshot.GetProperty("entries")[0].GetProperty("anilistId").GetInt32());
        var entriesToken = snapshot.GetProperty("nextPageToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(entriesToken));

        snapshot = await GetSnapshotAsync(client, $"/api/me/sync?limit=1&pageToken={Uri.EscapeDataString(entriesToken!)}");
        Assert.Equal(2, snapshot.GetProperty("entries")[0].GetProperty("anilistId").GetInt32());
        entriesToken = snapshot.GetProperty("nextPageToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(entriesToken));

        snapshot = await GetSnapshotAsync(client, $"/api/me/sync?limit=1&pageToken={Uri.EscapeDataString(entriesToken!)}");
        Assert.Equal(3, snapshot.GetProperty("entries")[0].GetProperty("anilistId").GetInt32());
        var resumeToken = snapshot.GetProperty("nextPageToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(resumeToken));

        snapshot = await GetSnapshotAsync(client, $"/api/me/sync?limit=1&pageToken={Uri.EscapeDataString(resumeToken!)}");
        Assert.Empty(snapshot.GetProperty("entries").EnumerateArray());
        var resumePage = snapshot.GetProperty("resumePositions");
        Assert.Single(resumePage.EnumerateArray());
        Assert.Equal(1, resumePage[0].GetProperty("episode").GetInt32());
        resumeToken = snapshot.GetProperty("nextPageToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(resumeToken));

        snapshot = await GetSnapshotAsync(client, $"/api/me/sync?limit=1&pageToken={Uri.EscapeDataString(resumeToken!)}");
        Assert.Equal(2, snapshot.GetProperty("resumePositions")[0].GetProperty("episode").GetInt32());
        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("nextPageToken").ValueKind);

        var patch = await client.PatchAsJsonAsync("/api/list/2", new { status = "Completed" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var delete = await client.DeleteAsync("/api/list/3");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var delta = await GetDeltaAsync(client, $"/api/me/sync?cursor={Uri.EscapeDataString(snapshotCursor!)}&limit=1");
        Assert.True(delta.GetProperty("hasMore").GetBoolean());
        var change = delta.GetProperty("changes")[0];
        Assert.Equal("2", change.GetProperty("entityKey").GetString());
        Assert.False(change.GetProperty("deleted").GetBoolean());
        Assert.Equal("Completed", change.GetProperty("snapshot").GetProperty("status").GetString());

        var nextCursor = delta.GetProperty("nextCursor").GetString();
        delta = await GetDeltaAsync(client, $"/api/me/sync?cursor={Uri.EscapeDataString(nextCursor!)}&limit=1");
        Assert.False(delta.GetProperty("hasMore").GetBoolean());
        var tombstone = delta.GetProperty("changes")[0];
        Assert.Equal("3", tombstone.GetProperty("entityKey").GetString());
        Assert.True(tombstone.GetProperty("deleted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, tombstone.GetProperty("snapshot").ValueKind);
    }

    [Fact]
    public async Task DeltaOlderThanRetentionReturnsResetRequired()
    {
        using var client = await TestUsers.CreateAndLoginAsync(_factory!, "sync-cursor-expiry@test.local");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/list", new { anilistId = 1 })).StatusCode);

        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SyncChanges.ExecuteUpdateAsync(update => update
                .SetProperty(change => change.CreatedAt, DateTimeOffset.UtcNow.AddDays(-91)));
        }

        using var expired = await client.GetAsync("/api/me/sync?cursor=0&limit=1");
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        using var expiredBody = JsonDocument.Parse(await expired.Content.ReadAsStringAsync());
        Assert.True(expiredBody.RootElement.GetProperty("resetRequired").GetBoolean());

        using var reset = await client.GetAsync("/api/me/sync?limit=1");
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var resetBody = JsonDocument.Parse(await reset.Content.ReadAsStringAsync());
        Assert.Equal(1, resetBody.RootElement.GetProperty("entries")[0].GetProperty("anilistId").GetInt32());
    }

    [Fact]
    public async Task ResumePositionsRejectStaleSequencesAndSupersededSessions()
    {
        using var client = await TestUsers.CreateAndLoginAsync(_factory!, "resume-ordering@test.local");
        var firstSession = Guid.NewGuid();
        var result = await SendEventAsync(new
        {
            eventId = Guid.NewGuid(), type = "resume.start", anilistId = 1, baseRevision = "0", episode = 1,
            sessionId = firstSession, positionMs = 1_000L, durationMs = 100_000L,
        });
        Assert.Equal("accepted", result.GetProperty("status").GetString());

        result = await SendEventAsync(new
        {
            eventId = Guid.NewGuid(), type = "resume.position", anilistId = 1, episode = 1,
            sessionId = firstSession, sequence = 2L, positionMs = 2_000L, durationMs = 100_000L,
        });
        Assert.Equal("accepted", result.GetProperty("status").GetString());

        result = await SendEventAsync(new
        {
            eventId = Guid.NewGuid(), type = "resume.position", anilistId = 1, episode = 1,
            sessionId = firstSession, sequence = 1L, positionMs = 1_500L, durationMs = 100_000L,
        });
        Assert.Equal("stale_sequence", result.GetProperty("code").GetString());

        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var position = await db.ResumePositions.SingleAsync(row => row.AnilistId == 1 && row.Episode == 1);
            Assert.Equal(firstSession, position.SessionId);
            Assert.Equal(2L, position.Sequence);
            Assert.Equal(2_000L, position.PositionMs);
            Assert.Equal(2, await db.SyncChanges.CountAsync(change => change.EntityType == "resumePosition"));
        }

        var nextSession = Guid.NewGuid();
        result = await SendEventAsync(new
        {
            eventId = Guid.NewGuid(), type = "resume.start", anilistId = 1, baseRevision = "2", episode = 1,
            sessionId = nextSession, positionMs = 0L, durationMs = 100_000L,
        });
        Assert.Equal("accepted", result.GetProperty("status").GetString());

        result = await SendEventAsync(new
        {
            eventId = Guid.NewGuid(), type = "resume.position", anilistId = 1, episode = 1,
            sessionId = firstSession, sequence = 3L, positionMs = 3_000L, durationMs = 100_000L,
        });
        Assert.Equal("session_superseded", result.GetProperty("code").GetString());

        async Task<JsonElement> SendEventAsync(object syncEvent)
        {
            using var response = await client.PostAsJsonAsync("/api/me/sync/events", new
            {
                contractVersion = 1,
                events = new[] { syncEvent },
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("results")[0].Clone();
        }
    }

    private static async Task<JsonElement> GetSnapshotAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private static async Task<JsonElement> GetDeltaAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }
}
