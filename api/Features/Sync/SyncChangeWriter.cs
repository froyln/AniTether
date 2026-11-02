using System.Text.Json;
using System.Globalization;
using AniTether.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AniTether.Api.Features.Sync;

public static class SyncChangeWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<long> RecordListEntryAsync(AppDbContext db, ListEntry entry, bool manualProgress, CancellationToken ct)
    {
        var cursor = await NextCursorAsync(db, entry.UserId, ct);
        entry.Revision = cursor;
        if (manualProgress) entry.ManualProgressRevision = cursor;

        var tombstone = await db.SyncTombstones.FindAsync([entry.UserId, entry.AnilistId], ct);
        if (tombstone is not null) db.SyncTombstones.Remove(tombstone);

        db.SyncChanges.Add(new SyncChange
        {
            UserId = entry.UserId,
            Cursor = cursor,
            EntityType = "listEntry",
            EntityKey = entry.AnilistId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CreatedAt = DateTimeOffset.UtcNow,
            SnapshotJson = JsonSerializer.Serialize(new
            {
                anilistId = entry.AnilistId,
                status = entry.Status.ToString(),
                progress = entry.Progress,
                score = entry.Score,
                notes = entry.Notes,
                startedAt = entry.StartedAt,
                finishedAt = entry.FinishedAt,
                updatedAt = entry.UpdatedAt,
                lastSourceId = entry.LastSourceId,
                revision = cursor.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }, JsonOptions),
        });
        return cursor;
    }

    public static async Task<long> RecordListDeletionAsync(AppDbContext db, ListEntry entry, CancellationToken ct)
    {
        var cursor = await NextCursorAsync(db, entry.UserId, ct);
        var tombstone = await db.SyncTombstones.FindAsync([entry.UserId, entry.AnilistId], ct);
        if (tombstone is null)
        {
            db.SyncTombstones.Add(new SyncTombstone { UserId = entry.UserId, AnilistId = entry.AnilistId, Revision = cursor });
        }
        else
        {
            tombstone.Revision = cursor;
        }

        db.SyncChanges.Add(new SyncChange
        {
            UserId = entry.UserId,
            Cursor = cursor,
            EntityType = "listEntry",
            EntityKey = entry.AnilistId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            IsDeleted = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return cursor;
    }

    public static async Task<long> RecordResumePositionAsync(AppDbContext db, ResumePosition position, CancellationToken ct)
    {
        var cursor = await NextCursorAsync(db, position.UserId, ct);
        position.Revision = cursor;
        db.SyncChanges.Add(new SyncChange
        {
            UserId = position.UserId,
            Cursor = cursor,
            EntityType = "resumePosition",
            EntityKey = $"{position.AnilistId.ToString(CultureInfo.InvariantCulture)}:{position.Episode.ToString(CultureInfo.InvariantCulture)}",
            SnapshotJson = JsonSerializer.Serialize(new
            {
                anilistId = position.AnilistId,
                episode = position.Episode,
                sessionId = position.SessionId,
                revision = cursor.ToString(CultureInfo.InvariantCulture),
                sequence = position.Sequence,
                positionMs = position.PositionMs,
                durationMs = position.DurationMs,
            }, JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return cursor;
    }

    public static async Task LockAccountAsync(AppDbContext db, string userId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Sync changes must share the list write transaction.");
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO sync_account_states (\"UserId\", \"Cursor\") VALUES ({userId}, 0) ON CONFLICT (\"UserId\") DO NOTHING",
            ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction.GetDbTransaction();
        command.CommandText = "SELECT \"Cursor\" FROM sync_account_states WHERE \"UserId\" = @userId FOR UPDATE";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "userId";
        parameter.Value = userId;
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(ct);
    }

    private static async Task<long> NextCursorAsync(AppDbContext db, string userId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Sync changes must share the list write transaction.");
        }

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "INSERT INTO sync_account_states (\"UserId\", \"Cursor\") VALUES (@userId, 1) " +
                              "ON CONFLICT (\"UserId\") DO UPDATE SET \"Cursor\" = sync_account_states.\"Cursor\" + 1 " +
                              "RETURNING \"Cursor\"";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "userId";
        parameter.Value = userId;
        command.Parameters.Add(parameter);
        var cursor = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt64(cursor, CultureInfo.InvariantCulture);
    }
}
