using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AniTether.Api.Data;
using AniTether.Api.Features.Anime;
using AniTether.Api.Features.List;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AniTether.Api.Features.Sync;

public static class SyncEndpoints
{
    private const int MaxEvents = 100;
    private const int MaxPageSize = 100;
    private static readonly TimeSpan ChangeRetention = TimeSpan.FromDays(90);
    private const int MaxNotesLength = 2000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/mobile/capabilities", async (AppDbContext db, HttpResponse response, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "no-store";
            return Results.Ok(new MobileCapabilitiesResponse(1,
                new MobileSyncCapability(1, true,
                    ["listEntry.add", "listEntry.patch", "listEntry.applyProgress", "listEntry.delete", "resume.start", "resume.position"],
                    true, true, 90),
                new MobileMediaCapability(1, [])));
        }).RequireAuthorization();

        var sync = app.MapGroup("/api/me/sync")
            .RequireAuthorization()
            .RequireRateLimiting("sync");

        sync.MapPost("/events", async (SyncBatchRequest request, HttpResponse response, ClaimsPrincipal principal,
                UserManager<ApplicationUser> users, AppDbContext db, CancellationToken ct) =>
            {
                response.Headers.CacheControl = "no-store";
                if (request.ContractVersion != 1 || request.Events is null || request.Events.Count is < 1 or > MaxEvents)
                {
                    return Results.BadRequest(new { error = "Expected contractVersion 1 and 1-100 events." });
                }

                var userId = users.GetUserId(principal)!;
                var results = new List<SyncEventResult>(request.Events.Count);
                foreach (var syncEvent in request.Events)
                {
                    if (syncEvent is null)
                    {
                        results.Add(Rejected(Guid.Empty, "invalid_event"));
                        continue;
                    }
                    results.Add(await ProcessEventAsync(db, userId, syncEvent, ct));
                }

                return Results.Ok(new SyncBatchResponse(1, results));
            });

        sync.MapGet("", async (string? cursor, string? pageToken, int? limit, HttpResponse response, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, AppDbContext db, IDataProtectionProvider protection, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "no-store";
            var pageSize = limit ?? MaxPageSize;
            if (pageSize is < 1 or > MaxPageSize || (cursor is not null && pageToken is not null))
            {
                return Results.BadRequest(new { error = "Limit must be 1-100; choose cursor or pageToken." });
            }

            var userId = users.GetUserId(principal)!;
            var protector = protection.CreateProtector("AniTether.MobileSync.Snapshot.v1");
            SnapshotPageToken? token = null;
            if (pageToken is not null)
            {
                try
                {
                    token = JsonSerializer.Deserialize<SnapshotPageToken>(protector.Unprotect(pageToken), JsonOptions);
                }
                catch (CryptographicException)
                {
                    return Results.BadRequest(new { error = "Invalid page token." });
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new { error = "Invalid page token." });
                }

                if (token is null || token.UserId != userId)
                {
                    return Results.BadRequest(new { error = "Invalid page token." });
                }
                if (token.Phase is not ("entries" or "resume"))
                {
                    return Results.BadRequest(new { error = "Invalid page token." });
                }
            }

            long requestedCursor = 0;
            if (cursor is not null && !TryRevision(cursor, out requestedCursor))
            {
                return Results.BadRequest(new { error = "Invalid cursor." });
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await SyncChangeWriter.LockAccountAsync(db, userId, ct);
            var state = await db.SyncAccountStates.SingleAsync(s => s.UserId == userId, ct);
            await PruneHistoryAsync(db, state, ct);

            if (cursor is not null)
            {
                if (requestedCursor > state.Cursor)
                {
                    return Results.BadRequest(new { error = "Cursor is ahead of the account." });
                }
                if (requestedCursor < state.HistoryFloor)
                {
                    await transaction.CommitAsync(ct);
                    db.ChangeTracker.Clear();
                    return Results.Json(new { resetRequired = true }, statusCode: StatusCodes.Status410Gone);
                }

                var changes = await db.SyncChanges.AsNoTracking()
                    .Where(c => c.UserId == userId && c.Cursor > requestedCursor)
                    .OrderBy(c => c.Cursor)
                    .Take(pageSize + 1)
                    .ToListAsync(ct);
                var hasMore = changes.Count > pageSize;
                var page = changes.Take(pageSize).Select(ToResponse).ToList();
                var nextCursor = page.Count > 0 ? page[^1].Cursor : cursor;
                await transaction.CommitAsync(ct);
                db.ChangeTracker.Clear();
                return Results.Ok(new SyncDeltaResponse(1, nextCursor, state.Cursor.ToString(CultureInfo.InvariantCulture), page, hasMore));
            }

            var snapshotCursor = token?.SnapshotCursor ?? state.Cursor;
            var afterAnilistId = token?.LastAnilistId ?? 0;
            if (snapshotCursor < state.HistoryFloor || snapshotCursor > state.Cursor)
            {
                await transaction.CommitAsync(ct);
                db.ChangeTracker.Clear();
                return Results.Json(new { resetRequired = true }, statusCode: StatusCodes.Status410Gone);
            }

            if (token?.Phase == "resume")
            {
                var positions = await db.ResumePositions.AsNoTracking()
                    .Where(r => r.UserId == userId &&
                        (r.AnilistId > afterAnilistId || (r.AnilistId == afterAnilistId && r.Episode > token.LastEpisode)))
                    .OrderBy(r => r.AnilistId).ThenBy(r => r.Episode)
                    .Take(pageSize + 1)
                    .ToListAsync(ct);
                var hasMorePositions = positions.Count > pageSize;
                var resumePage = positions.Take(pageSize).Select(ToResponse).ToList();
                string? resumeNextPageToken = null;
                if (hasMorePositions)
                {
                    var last = resumePage[^1];
                    resumeNextPageToken = protector.Protect(JsonSerializer.Serialize(
                        new SnapshotPageToken(userId, snapshotCursor, "resume", last.AnilistId, last.Episode), JsonOptions));
                }

                await transaction.CommitAsync(ct);
                db.ChangeTracker.Clear();
                return Results.Ok(new SyncSnapshotResponse(1, snapshotCursor.ToString(CultureInfo.InvariantCulture), [], resumePage, resumeNextPageToken));
            }

            var entries = await db.ListEntries
                .Include(e => e.Anime)
                .Where(e => e.UserId == userId && e.AnilistId > afterAnilistId)
                .OrderBy(e => e.AnilistId)
                .Take(pageSize + 1)
                .ToListAsync(ct);
            var hasNextPage = entries.Count > pageSize;
            var entryPage = entries.Take(pageSize).ToList();
            var ids = entryPage.Select(e => e.AnilistId).ToList();
            var links = await db.AnimeSourceLinks.Where(l => ids.Contains(l.AnilistId)).ToListAsync(ct);
            var linksByAnime = links.ToLookup(l => l.AnilistId);
            var responseEntries = entryPage.Select(e => ListEntryResponse.From(e, linksByAnime[e.AnilistId])).ToList();
            string? nextPageToken = null;
            if (hasNextPage)
            {
                var next = new SnapshotPageToken(userId, snapshotCursor, "entries", entryPage[^1].AnilistId, 0);
                nextPageToken = protector.Protect(JsonSerializer.Serialize(next, JsonOptions));
            }
            else if (await db.ResumePositions.AnyAsync(r => r.UserId == userId, ct))
            {
                nextPageToken = protector.Protect(JsonSerializer.Serialize(
                    new SnapshotPageToken(userId, snapshotCursor, "resume", 0, 0), JsonOptions));
            }

            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return Results.Ok(new SyncSnapshotResponse(1, snapshotCursor.ToString(CultureInfo.InvariantCulture), responseEntries, [], nextPageToken));
        });
    }

    private static async Task PruneHistoryAsync(AppDbContext db, SyncAccountState state, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - ChangeRetention;
        var expiredThrough = await db.SyncChanges
            .Where(c => c.UserId == state.UserId && c.CreatedAt < cutoff)
            .MaxAsync(c => (long?)c.Cursor, ct);
        if (expiredThrough is null || expiredThrough <= state.HistoryFloor) return;

        var expired = await db.SyncChanges
            .Where(c => c.UserId == state.UserId && c.Cursor <= expiredThrough)
            .ToListAsync(ct);
        db.SyncChanges.RemoveRange(expired);
        state.HistoryFloor = expiredThrough.Value;
        await db.SaveChangesAsync(ct);
    }

    private static SyncChangeResponse ToResponse(SyncChange change)
    {
        JsonElement? snapshot = change.SnapshotJson is null
            ? null
            : JsonSerializer.Deserialize<JsonElement>(change.SnapshotJson, JsonOptions);
        return new SyncChangeResponse(change.Cursor.ToString(CultureInfo.InvariantCulture), change.EntityType,
            change.EntityKey, change.Cursor.ToString(CultureInfo.InvariantCulture), change.IsDeleted, snapshot);
    }

    private static ResumePositionResponse ToResponse(ResumePosition position) => new(position.AnilistId,
        position.Episode, position.SessionId, position.Revision.ToString(CultureInfo.InvariantCulture),
        position.Sequence, position.PositionMs, position.DurationMs);

    private static async Task<SyncEventResult> ProcessEventAsync(AppDbContext db, string userId,
        SyncEventRequest syncEvent, CancellationToken ct, int collisionRetry = 0)
    {
        if (syncEvent.EventId == Guid.Empty)
        {
            return Rejected(syncEvent.EventId, "invalid_event_id");
        }
        if (string.IsNullOrWhiteSpace(syncEvent.Type)) return Rejected(syncEvent.EventId, "invalid_event_type");

        var fingerprint = Fingerprint(syncEvent);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.SyncEventReceipts.FindAsync([userId, syncEvent.EventId], ct);
        if (existing is not null)
        {
            await transaction.RollbackAsync(ct);
            await transaction.DisposeAsync();
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(existing.Fingerprint), Convert.FromHexString(fingerprint)))
            {
                return Rejected(syncEvent.EventId, "event_id_reused");
            }

            var replay = await ReplayAsync(db, userId, existing, ct);
            db.ChangeTracker.Clear();
            return replay;
        }

        await SyncChangeWriter.LockAccountAsync(db, userId, ct);
        existing = await db.SyncEventReceipts.FindAsync([userId, syncEvent.EventId], ct);
        if (existing is not null)
        {
            await transaction.RollbackAsync(ct);
            await transaction.DisposeAsync();
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(existing.Fingerprint), Convert.FromHexString(fingerprint)))
            {
                return Rejected(syncEvent.EventId, "event_id_reused");
            }

            var replay = await ReplayAsync(db, userId, existing, ct);
            db.ChangeTracker.Clear();
            return replay;
        }

        var result = await ApplyEventAsync(db, userId, syncEvent, ct);
        db.SyncEventReceipts.Add(new SyncEventReceipt
        {
            UserId = userId,
            EventId = syncEvent.EventId,
            AnilistId = syncEvent.AnilistId,
            EntityType = syncEvent.Type.StartsWith("resume.", StringComparison.Ordinal) ? "resumePosition" : "listEntry",
            Episode = syncEvent.Episode,
            Fingerprint = fingerprint,
            Outcome = result.Code is null ? result.Status : $"{result.Status}:{result.Code}",
            Revision = result.Revision,
            Cursor = result.Cursor,
        });

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return result;
        }
        catch (DbUpdateException e) when (AppDbContext.IsUniqueViolation(e))
        {
            await transaction.RollbackAsync(ct);
            await transaction.DisposeAsync();
            db.ChangeTracker.Clear();
            var raced = await db.SyncEventReceipts.FindAsync([userId, syncEvent.EventId], ct);
            if (raced is not null && !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(raced.Fingerprint), Convert.FromHexString(fingerprint)))
            {
                return Rejected(syncEvent.EventId, "event_id_reused");
            }

            if (raced is not null)
            {
                var replay = await ReplayAsync(db, userId, raced, ct);
                db.ChangeTracker.Clear();
                return replay;
            }
            if (collisionRetry > 0) throw;
            await transaction.DisposeAsync();
            return await ProcessEventAsync(db, userId, syncEvent, ct, collisionRetry + 1);
        }
    }

    private static async Task<SyncEventResult> ApplyEventAsync(AppDbContext db, string userId,
        SyncEventRequest syncEvent, CancellationToken ct)
    {
        if (syncEvent.AnilistId < 1) return Rejected(syncEvent.EventId, "invalid_anilist_id");

        switch (syncEvent.Type)
        {
            case "listEntry.add":
            {
                if (!TryRevision(syncEvent.BaseRevision, out var baseRevision)) return Rejected(syncEvent.EventId, "invalid_revision");
                if (await db.ListEntries.AnyAsync(e => e.UserId == userId && e.AnilistId == syncEvent.AnilistId, ct))
                {
                    return await ConflictAsync(db, userId, syncEvent, "entry_exists", ct);
                }

                var tombstone = await db.SyncTombstones.FindAsync([userId, syncEvent.AnilistId], ct);
                var currentRevision = tombstone?.Revision ?? 0;
                if (baseRevision != currentRevision) return await ConflictAsync(db, userId, syncEvent, "revision_mismatch", ct);
                var anime = await db.Anime.FindAsync([syncEvent.AnilistId], ct);
                if (anime is null) return Rejected(syncEvent.EventId, "unknown_anime");

                var entry = new ListEntry
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    AnilistId = syncEvent.AnilistId,
                    Anime = anime,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
                db.ListEntries.Add(entry);
                var cursor = await SyncChangeWriter.RecordListEntryAsync(db, entry, manualProgress: false, ct);
                return Accepted(syncEvent.EventId, cursor);
            }
            case "listEntry.patch":
            {
                if (!TryRevision(syncEvent.BaseRevision, out var baseRevision)) return Rejected(syncEvent.EventId, "invalid_revision");
                if (!HasPatch(syncEvent)) return Rejected(syncEvent.EventId, "empty_patch");
                if (syncEvent.Progress is < 0 or > ListEntry.MaxEpisode) return Rejected(syncEvent.EventId, "invalid_progress");
                if (syncEvent.Score is < 0 or > ListEntry.MaxScore) return Rejected(syncEvent.EventId, "invalid_score");
                if (syncEvent.Notes?.Length > MaxNotesLength) return Rejected(syncEvent.EventId, "notes_too_long");
                if (syncEvent.Status is not null && !TryStatus(syncEvent.Status, out _)) return Rejected(syncEvent.EventId, "invalid_status");
                if (syncEvent.LastSourceId is Guid sourceId && !await db.Sources.AnyAsync(s => s.Id == sourceId, ct))
                {
                    return Rejected(syncEvent.EventId, "unknown_source");
                }

                var entry = await db.ListEntries.Include(e => e.Anime)
                    .FirstOrDefaultAsync(e => e.UserId == userId && e.AnilistId == syncEvent.AnilistId, ct);
                if (entry is null || entry.Revision != baseRevision)
                {
                    return await ConflictAsync(db, userId, syncEvent, entry is null ? "entry_missing" : "revision_mismatch", ct);
                }

                if (syncEvent.Progress is int requestedProgress && requestedProgress > entry.Progress &&
                    !AnimeAiring.HasAired(entry.Anime!, requestedProgress))
                {
                    return Rejected(syncEvent.EventId, "episode_not_aired");
                }

                if (syncEvent.Progress is int progress) entry.SetProgress(progress, entry.Anime!.Episodes);
                if (syncEvent.Status is not null && TryStatus(syncEvent.Status, out var status)) entry.Status = status;
                if (syncEvent.Score is int score) entry.Score = score;
                if (syncEvent.Notes is not null) entry.Notes = syncEvent.Notes;
                if (syncEvent.LastSourceId is Guid lastSourceId) entry.LastSourceId = lastSourceId;
                entry.UpdatedAt = DateTimeOffset.UtcNow;

                var cursor = await SyncChangeWriter.RecordListEntryAsync(db, entry, syncEvent.Progress.HasValue, ct);
                return Accepted(syncEvent.EventId, cursor);
            }
            case "listEntry.applyProgress":
            {
                if (!TryRevision(syncEvent.ObservedRevision, out var observedRevision)) return Rejected(syncEvent.EventId, "invalid_revision");
                if (syncEvent.Episode is not int episode || episode is < 1 or > ListEntry.MaxEpisode) return Rejected(syncEvent.EventId, "invalid_progress");
                if (syncEvent.LastSourceId is Guid sourceId && !await db.Sources.AnyAsync(s => s.Id == sourceId, ct))
                {
                    return Rejected(syncEvent.EventId, "unknown_source");
                }

                var entry = await db.ListEntries.Include(e => e.Anime)
                    .FirstOrDefaultAsync(e => e.UserId == userId && e.AnilistId == syncEvent.AnilistId, ct);
                if (entry is null) return await ConflictAsync(db, userId, syncEvent, "entry_missing", ct);
                if (episode > entry.Progress && !AnimeAiring.HasAired(entry.Anime!, episode))
                {
                    return Rejected(syncEvent.EventId, "episode_not_aired");
                }
                if (entry.ManualProgressRevision is long manualRevision && manualRevision > observedRevision)
                {
                    return await ConflictAsync(db, userId, syncEvent, "manual_progress_changed", ct);
                }

                entry.ApplyProgress(episode, entry.Anime!.Episodes);
                if (syncEvent.LastSourceId is Guid lastSourceId) entry.LastSourceId = lastSourceId;
                var cursor = await SyncChangeWriter.RecordListEntryAsync(db, entry, manualProgress: false, ct);
                return Accepted(syncEvent.EventId, cursor);
            }
            case "listEntry.delete":
            {
                if (!TryRevision(syncEvent.BaseRevision, out var baseRevision)) return Rejected(syncEvent.EventId, "invalid_revision");
                var entry = await db.ListEntries.FirstOrDefaultAsync(e => e.UserId == userId && e.AnilistId == syncEvent.AnilistId, ct);
                if (entry is null || entry.Revision != baseRevision)
                {
                    return await ConflictAsync(db, userId, syncEvent, entry is null ? "entry_missing" : "revision_mismatch", ct);
                }

                var cursor = await SyncChangeWriter.RecordListDeletionAsync(db, entry, ct);
                db.ListEntries.Remove(entry);
                return Accepted(syncEvent.EventId, cursor);
            }
            case "resume.start":
            {
                if (!TryRevision(syncEvent.BaseRevision, out var baseRevision)) return Rejected(syncEvent.EventId, "invalid_revision");
                if (syncEvent.Episode is not int episode || episode is < 1 or > ListEntry.MaxEpisode) return Rejected(syncEvent.EventId, "invalid_episode");
                if (syncEvent.SessionId is not Guid sessionId || sessionId == Guid.Empty) return Rejected(syncEvent.EventId, "invalid_session_id");
                if (!ValidPosition(syncEvent.PositionMs, syncEvent.DurationMs)) return Rejected(syncEvent.EventId, "invalid_position");

                var position = await db.ResumePositions.FirstOrDefaultAsync(
                    r => r.UserId == userId && r.AnilistId == syncEvent.AnilistId && r.Episode == episode, ct);
                if ((position?.Revision ?? 0) != baseRevision)
                {
                    return await ConflictAsync(db, userId, syncEvent, "revision_mismatch", ct);
                }
                if (position?.SessionId == sessionId) return Rejected(syncEvent.EventId, "session_id_reused");
                var anime = await db.Anime.FindAsync([syncEvent.AnilistId], ct);
                if (anime is null) return Rejected(syncEvent.EventId, "unknown_anime");
                if (!AnimeAiring.HasAired(anime, episode)) return Rejected(syncEvent.EventId, "episode_not_aired");

                if (position is null)
                {
                    position = new ResumePosition { UserId = userId, AnilistId = syncEvent.AnilistId, Episode = episode };
                    db.ResumePositions.Add(position);
                }
                position.SessionId = sessionId;
                position.Sequence = 0;
                position.PositionMs = syncEvent.PositionMs!.Value;
                position.DurationMs = syncEvent.DurationMs;
                var cursor = await SyncChangeWriter.RecordResumePositionAsync(db, position, ct);
                return Accepted(syncEvent.EventId, cursor);
            }
            case "resume.position":
            {
                if (syncEvent.Episode is not int episode || episode is < 1 or > ListEntry.MaxEpisode) return Rejected(syncEvent.EventId, "invalid_episode");
                if (syncEvent.SessionId is not Guid sessionId || sessionId == Guid.Empty) return Rejected(syncEvent.EventId, "invalid_session_id");
                if (syncEvent.Sequence is not long sequence || sequence < 1) return Rejected(syncEvent.EventId, "invalid_sequence");
                if (!ValidPosition(syncEvent.PositionMs, syncEvent.DurationMs)) return Rejected(syncEvent.EventId, "invalid_position");

                var position = await db.ResumePositions.FirstOrDefaultAsync(
                    r => r.UserId == userId && r.AnilistId == syncEvent.AnilistId && r.Episode == episode, ct);
                if (position is null) return await ConflictAsync(db, userId, syncEvent, "session_missing", ct);
                if (position.SessionId != sessionId) return await ConflictAsync(db, userId, syncEvent, "session_superseded", ct);
                if (sequence <= position.Sequence)
                {
                    var revision = position.Revision.ToString(CultureInfo.InvariantCulture);
                    return new SyncEventResult(syncEvent.EventId, "accepted", "stale_sequence", revision, position.Revision,
                        await CurrentStateAsync(db, userId, syncEvent.AnilistId, "resumePosition", episode, ct));
                }

                position.Sequence = sequence;
                position.PositionMs = syncEvent.PositionMs!.Value;
                if (syncEvent.DurationMs.HasValue) position.DurationMs = syncEvent.DurationMs;
                var cursor = await SyncChangeWriter.RecordResumePositionAsync(db, position, ct);
                return Accepted(syncEvent.EventId, cursor);
            }
            default:
                return Rejected(syncEvent.EventId, "unsupported_event_type");
        }
    }

    private static bool ValidPosition(long? positionMs, long? durationMs) =>
        positionMs is >= 0 && (durationMs is null or > 0) &&
        (durationMs is null || positionMs <= durationMs);

    private static async Task<SyncEventResult> ReplayAsync(AppDbContext db, string userId,
        SyncEventReceipt receipt, CancellationToken ct)
    {
        var separator = receipt.Outcome.IndexOf(':');
        var status = separator < 0 ? receipt.Outcome : receipt.Outcome[..separator];
        var code = separator < 0 ? null : receipt.Outcome[(separator + 1)..];
        object? current = status == "conflict"
            ? await CurrentStateAsync(db, userId, receipt.AnilistId, receipt.EntityType, receipt.Episode, ct)
            : null;
        return new SyncEventResult(receipt.EventId, status, code, receipt.Revision, receipt.Cursor, current);
    }

    private static async Task<SyncEventResult> ConflictAsync(AppDbContext db, string userId,
        SyncEventRequest syncEvent, string code, CancellationToken ct) =>
        new(syncEvent.EventId, "conflict", code, null, null,
            await CurrentStateAsync(db, userId, syncEvent.AnilistId,
                syncEvent.Type.StartsWith("resume.", StringComparison.Ordinal) ? "resumePosition" : "listEntry",
                syncEvent.Episode, ct));

    private static async Task<object?> CurrentStateAsync(AppDbContext db, string userId, int anilistId,
        string entityType, int? episode, CancellationToken ct)
    {
        if (entityType == "resumePosition" && episode is int resumeEpisode)
        {
            var position = await db.ResumePositions.FirstOrDefaultAsync(
                r => r.UserId == userId && r.AnilistId == anilistId && r.Episode == resumeEpisode, ct);
            return position is null ? null : new
            {
                anilistId,
                position.Episode,
                position.SessionId,
                revision = position.Revision.ToString(CultureInfo.InvariantCulture),
                position.Sequence,
                position.PositionMs,
                position.DurationMs,
            };
        }

        var entry = await db.ListEntries.FirstOrDefaultAsync(e => e.UserId == userId && e.AnilistId == anilistId, ct);
        if (entry is not null) return new { anilistId, revision = entry.Revision.ToString(CultureInfo.InvariantCulture), status = entry.Status.ToString(), entry.Progress, entry.Score, entry.Notes, entry.StartedAt, entry.FinishedAt, entry.UpdatedAt, entry.LastSourceId };
        var tombstone = await db.SyncTombstones.FindAsync([userId, anilistId], ct);
        return tombstone is null ? null : new { anilistId, revision = tombstone.Revision.ToString(CultureInfo.InvariantCulture), deleted = true };
    }

    private static string Fingerprint(SyncEventRequest syncEvent) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(syncEvent, JsonOptions)));

    private static bool TryRevision(string? value, out long revision) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out revision) && revision >= 0;

    private static bool TryStatus(string value, out ListStatus status) =>
        Enum.TryParse(value, ignoreCase: false, out status) && Enum.IsDefined(status);

    private static bool HasPatch(SyncEventRequest syncEvent) => syncEvent.Status is not null || syncEvent.Progress.HasValue ||
        syncEvent.Score.HasValue || syncEvent.Notes is not null || syncEvent.LastSourceId.HasValue;

    private static SyncEventResult Accepted(Guid eventId, long cursor) =>
        new(eventId, "accepted", null, cursor.ToString(CultureInfo.InvariantCulture), cursor, null);

    private static SyncEventResult Rejected(Guid eventId, string code) => new(eventId, "rejected", code, null, null, null);
}

public sealed record SyncBatchRequest(int ContractVersion, List<SyncEventRequest?>? Events);
public sealed class SyncEventRequest
{
    public Guid EventId { get; init; }
    public string Type { get; init; } = "";
    public int AnilistId { get; init; }
    public string? BaseRevision { get; init; }
    public string? ObservedRevision { get; init; }
    public string? Status { get; init; }
    public int? Progress { get; init; }
    public int? Score { get; init; }
    public string? Notes { get; init; }
    public Guid? LastSourceId { get; init; }
    public int? Episode { get; init; }
    public Guid? SessionId { get; init; }
    public long? Sequence { get; init; }
    public long? PositionMs { get; init; }
    public long? DurationMs { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}
public sealed record SyncEventResult(Guid EventId, string Status, string? Code, string? Revision,
    long? Cursor, object? Current);
public sealed record SyncBatchResponse(int ContractVersion, IReadOnlyList<SyncEventResult> Results);
public sealed record SyncSnapshotResponse(int ContractVersion, string SnapshotCursor,
    IReadOnlyList<ListEntryResponse> Entries, IReadOnlyList<ResumePositionResponse> ResumePositions, string? NextPageToken);
public sealed record SyncChangeResponse(string Cursor, string EntityType, string EntityKey,
    string Revision, bool Deleted, JsonElement? Snapshot);
public sealed record SyncDeltaResponse(int ContractVersion, string NextCursor, string CurrentCursor,
    IReadOnlyList<SyncChangeResponse> Changes, bool HasMore);
public sealed record ResumePositionResponse(int AnilistId, int Episode, Guid SessionId, string Revision,
    long Sequence, long PositionMs, long? DurationMs);
public sealed record SnapshotPageToken(string UserId, long SnapshotCursor, string Phase, int LastAnilistId, int LastEpisode);

public sealed record MobileCapabilitiesResponse(int ContractVersion, MobileSyncCapability Sync, MobileMediaCapability Media);
public sealed record MobileSyncCapability(int Version, bool Ready, IReadOnlyList<string> SupportedEvents,
    bool SnapshotAndDelta, bool ResumeSessions, int CursorRetentionDays);
public sealed record MobileMediaCapability(int Version, IReadOnlyList<MobileMediaAdapterCapability> Adapters);
public sealed record MobileMediaAdapterCapability(string Id, Guid SourceId, string Protocol, string MediaHost,
    string Referer, string Origin, string UserAgent, IReadOnlyList<MobileMediaRedirect> Redirects,
    IReadOnlyList<string> Qualities, IReadOnlyList<string> Audio, string SubtitleMode, string SubtitleLanguage);
public sealed record MobileMediaRedirect(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Host,
    int Port,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HostPattern = null);
