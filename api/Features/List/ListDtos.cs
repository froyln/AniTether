using AniTether.Api.Data;
using AniTether.Api.Features.Sources;
using System.Globalization;

namespace AniTether.Api.Features.List;

public record CreateListEntryRequest(int AnilistId);

public record UpdateListEntryRequest(ListStatus? Status, int? Progress, int? Score, string? Notes, Guid? LastSourceId);
public record ApplyProgressRequest(int Episode, Guid? LastSourceId, string? ObservedRevision = null);

public record ListEntryResponse(
    int AnilistId,
    string TitleRomaji,
    string? TitleEnglish,
    int? MalId,
    string? CoverUrl,
    int? Episodes,
    ListStatus Status,
    int Progress,
    int? Score,
    string? Notes,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset UpdatedAt,
    Guid? LastSourceId,
    IReadOnlyList<AnimeSourceLinkResponse> SourceLinks,
    string Revision = "0")
{
    public static ListEntryResponse From(ListEntry entry, IEnumerable<AnimeSourceLink> links) => new(
        entry.AnilistId,
        entry.Anime!.TitleRomaji,
        entry.Anime.TitleEnglish,
        entry.Anime.MalId,
        entry.Anime.CoverUrl,
        entry.Anime.Episodes,
        entry.Status,
        entry.Progress,
        entry.Score,
        entry.Notes,
        entry.StartedAt,
        entry.FinishedAt,
        entry.UpdatedAt,
        entry.LastSourceId,
        links.Select(AnimeSourceLinkResponse.From).ToList(),
        entry.Revision.ToString(CultureInfo.InvariantCulture));
}
