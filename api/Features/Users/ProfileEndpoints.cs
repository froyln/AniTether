using System.Buffers.Binary;
using System.Security.Claims;
using AniTether.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AniTether.Api.Features.Users;

public static class ProfileEndpoints
{
    private const int MaxAvatarBytes = 1_000_000;
    private const int MaxBioLength = 280;

    public static void MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/profiles/{id}", async (string id, UserManager<ApplicationUser> users, AppDbContext db, CancellationToken ct) =>
        {
            var user = await PublicUser(users, id, ct);
            return user is null ? Results.NotFound() : Results.Ok(await ReadProfile(db, user, false, ct));
        });

        app.MapGet("/api/profiles/{id}/avatar", async (string id, UserManager<ApplicationUser> users, AppDbContext db, CancellationToken ct) =>
        {
            var user = await PublicUser(users, id, ct);
            if (user?.AvatarUpdatedAt is null) return Results.NotFound();
            return await AvatarResult(db, id, ct);
        });

        var me = app.MapGroup("/api/me").RequireAuthorization();
        me.MapGet("/profile-preview", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users, AppDbContext db, CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            return user is null ? Results.Unauthorized() : Results.Ok(await ReadProfile(db, user, true, ct));
        });

        me.MapGet("/avatar", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users, AppDbContext db, CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            return user is null ? Results.Unauthorized() : await AvatarResult(db, user.Id, ct);
        });

        me.MapPut("/avatar", async (UploadAvatarRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            AppDbContext db, CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            if (request.Data is null || request.ContentType is null)
                return Results.BadRequest(new { error = "Avatar data is required." });
            if (request.Data.Length > (MaxAvatarBytes + 2) / 3 * 4 + 4)
                return Results.BadRequest(new { error = "Avatar must be no larger than 1 MB." });

            byte[] data;
            try { data = Convert.FromBase64String(request.Data); }
            catch (FormatException) { return Results.BadRequest(new { error = "Avatar data is invalid." }); }
            if (!ValidImage(data, request.ContentType))
                return Results.BadRequest(new { error = "Choose a PNG or JPEG image up to 1 MB and 2048 pixels per side." });

            var avatar = await db.UserAvatars.FindAsync([user.Id], ct);
            var now = DateTimeOffset.UtcNow;
            if (avatar is null)
            {
                db.UserAvatars.Add(new UserAvatar { UserId = user.Id, ContentType = request.ContentType, Data = data, UpdatedAt = now });
            }
            else
            {
                avatar.ContentType = request.ContentType;
                avatar.Data = data;
                avatar.UpdatedAt = now;
            }
            user.AvatarUpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireRateLimiting("auth");

        me.MapDelete("/avatar", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users, AppDbContext db, CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            var avatar = await db.UserAvatars.FindAsync([user.Id], ct);
            if (avatar is not null) db.UserAvatars.Remove(avatar);
            user.AvatarUpdatedAt = null;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireRateLimiting("auth");
    }

    private static async Task<ApplicationUser?> PublicUser(UserManager<ApplicationUser> users, string id, CancellationToken ct) =>
        await users.Users.FirstOrDefaultAsync(u => u.Id == id && u.ProfilePublic &&
            (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow), ct);

    private static async Task<PublicProfileResponse> ReadProfile(AppDbContext db, ApplicationUser user, bool preview, CancellationToken ct)
    {
        var entries = await db.ListEntries.AsNoTracking().Include(e => e.Anime).Where(e => e.UserId == user.Id)
            .OrderByDescending(e => e.UpdatedAt).Select(e => new PublicEntryResponse(e.AnilistId,
                e.Anime!.TitleRomaji, e.Anime.CoverUrl, e.Status, e.Progress, e.Score, e.UpdatedAt)).ToListAsync(ct);
        return new PublicProfileResponse(user.Id, user.DisplayName, user.ProfileBio,
            user.AvatarUpdatedAt?.ToUnixTimeMilliseconds(), entries, preview, user.ProfilePublic);
    }

    private static async Task<IResult> AvatarResult(AppDbContext db, string userId, CancellationToken ct)
    {
        var avatar = await db.UserAvatars.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);
        return avatar is null ? Results.NotFound() : Results.File(avatar.Data, avatar.ContentType);
    }

    private static bool ValidImage(byte[] data, string? contentType)
    {
        if (data.Length is < 24 or > MaxAvatarBytes) return false;
        uint width;
        uint height;
        if (contentType == "image/png" && data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            data.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            width = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16, 4));
            height = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20, 4));
        }
        else if (contentType == "image/jpeg" && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff &&
                 TryReadJpegDimensions(data, out width, out height))
        {
            // JPEG dimensions are read from a Start Of Frame segment below.
        }
        else return false;

        return width is > 0 and <= 2048 && height is > 0 and <= 2048 && (ulong)width * height <= 4_194_304;
    }

    private static bool TryReadJpegDimensions(byte[] data, out uint width, out uint height)
    {
        width = height = 0;
        var offset = 2;
        while (offset + 4 <= data.Length)
        {
            if (data[offset++] != 0xff) return false;
            while (offset < data.Length && data[offset] == 0xff) offset++;
            if (offset >= data.Length) return false;
            var marker = data[offset++];
            if (marker is 0xd8 or 0xd9 or 0x01 || marker is >= 0xd0 and <= 0xd7) continue;
            if (offset + 2 > data.Length) return false;
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
            if (segmentLength < 2 || offset + segmentLength > data.Length) return false;
            if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)
            {
                if (segmentLength < 7) return false;
                height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 5, 2));
                return true;
            }
            offset += segmentLength;
        }
        return false;
    }
}

public record UploadAvatarRequest(string ContentType, string Data);
public record PublicProfileResponse(string Id, string DisplayName, string? Bio, long? AvatarUpdatedAt,
    IReadOnlyList<PublicEntryResponse> Entries, bool IsPreview, bool IsPublic);
public record PublicEntryResponse(int AnilistId, string Title, string? CoverUrl, ListStatus Status,
    int Progress, int? Score, DateTimeOffset UpdatedAt);
