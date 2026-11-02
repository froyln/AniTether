using AniTether.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AniTether.Api.Features.Sync;

public sealed class SyncHistoryCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<SyncHistoryCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var cutoff = DateTimeOffset.UtcNow - Retention;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    WITH expired AS (
                        SELECT "UserId", MAX("Cursor") AS "Floor"
                        FROM sync_changes
                        WHERE "CreatedAt" < {cutoff}
                        GROUP BY "UserId"
                    ), deleted AS (
                        DELETE FROM sync_changes AS c
                        USING expired AS e
                        WHERE c."UserId" = e."UserId" AND c."Cursor" <= e."Floor"
                        RETURNING c."UserId"
                    )
                    UPDATE sync_account_states AS s
                    SET "HistoryFloor" = GREATEST(s."HistoryFloor", e."Floor")
                    FROM expired AS e
                    WHERE s."UserId" = e."UserId"
                    """, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not prune expired sync history.");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
