using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace AniTether.Api.Features.Anime;

public record AniListMedia(
    int Id,
    int? IdMal,
    [property: JsonPropertyName("title")] AniListTitle Title,
    [property: JsonPropertyName("coverImage")] AniListCoverImage? CoverImage,
    [property: JsonPropertyName("bannerImage")] string? BannerImage,
    string? Description,
    List<string>? Genres,
    int? Episodes,
    string? Format,
    string? Status,
    [property: JsonPropertyName("nextAiringEpisode")] AniListNextAiringEpisode? NextAiringEpisode);

public record AniListTitle(string Romaji, string? English);
public record AniListCoverImage(string? Large);
public record AniListNextAiringEpisode(int Episode);

public record AniListFuzzyDate(int? Year, int? Month, int? Day)
{
    public DateTimeOffset? ToDateTimeOffset() =>
        Year is int y ? new DateTimeOffset(y, Month ?? 1, Day ?? 1, 0, 0, 0, TimeSpan.Zero) : null;
}

public record AniListListEntry(
    string Status,
    int Progress,
    int? Score,
    [property: JsonPropertyName("startedAt")] AniListFuzzyDate? StartedAt,
    [property: JsonPropertyName("completedAt")] AniListFuzzyDate? CompletedAt,
    AniListMedia Media);

/// <summary>
/// ponytail: no outbound rate limiting — MVP traffic can't hit AniList's 90 req/min.
/// Upgrade path if that changes: wrap SendAsync with a PartitionedRateLimiter
/// (System.Threading.RateLimiting, already ships with ASP.NET Core).
/// </summary>
public class AniListClient(HttpClient http, IMemoryCache cache)
{
    private const string Endpoint = "https://graphql.anilist.co";

    private const string MediaFields = """
        id
        idMal
        title { romaji english }
        coverImage { large }
        bannerImage
        description(asHtml: false)
        genres
        episodes
        format
        status
        nextAiringEpisode { episode }
        """;

    private const string SearchQuery = $$"""
        query ($search: String) {
          Page(perPage: 10) {
            media(search: $search, type: ANIME) {
              {{MediaFields}}
            }
          }
        }
        """;

    // Unset filters are omitted from the variables (see OmitNullsOpts), so one query covers every combination.
    private const string BrowseQuery = $$"""
        query ($page: Int, $sort: [MediaSort], $search: String, $genres: [String], $format: MediaFormat,
               $year: Int, $season: MediaSeason, $status: MediaStatus) {
          Page(page: $page, perPage: 20) {
            media(type: ANIME, isAdult: false, sort: $sort, search: $search, genre_in: $genres, format: $format,
                  seasonYear: $year, season: $season, status: $status) {
              {{MediaFields}}
            }
          }
        }
        """;

    private const string ByIdQuery = $$"""
        query ($id: Int) {
          Media(id: $id, type: ANIME) {
            {{MediaFields}}
          }
        }
        """;

    private const string MediaListQuery = $$"""
        query ($userName: String) {
          MediaListCollection(userName: $userName, type: ANIME) {
            lists {
              entries {
                status
                progress
                score(format: POINT_10)
                startedAt { year month day }
                completedAt { year month day }
                media {
                  {{MediaFields}}
                }
              }
            }
          }
        }
        """;

    private const string ByMalIdsQuery = $$"""
        query ($ids: [Int]) {
          Page(perPage: 50) {
            media(idMal_in: $ids, type: ANIME) {
              {{MediaFields}}
            }
          }
        }
        """;

    public async Task<IReadOnlyList<AniListMedia>> SearchAsync(string title, CancellationToken ct = default) =>
        PageMedia(await PostAsync(SearchQuery, new { search = title }, ct));

    /// <summary>Filtered catalog browse/search, paged 20 per page.</summary>
    public async Task<IReadOnlyList<AniListMedia>> BrowseAsync(int page, CatalogFilter filter, CancellationToken ct = default)
    {
        var variables = new
        {
            page,
            sort = new[] { filter.Sort },
            search = filter.Search,
            genres = filter.Genres,
            format = filter.Format,
            year = filter.Year,
            season = filter.Season,
            status = filter.Status,
        };
        return PageMedia(await PostAsync(BrowseQuery, variables, ct, OmitNullsOpts));
    }

    /// <summary>Null when AniList has no anime with that id.</summary>
    public async Task<AniListMedia?> GetByIdAsync(int anilistId, CancellationToken ct = default) =>
        await PostAsync(ByIdQuery, new { id = anilistId }, ct) is { } data && data.GetProperty("Media") is { ValueKind: JsonValueKind.Object } media
            ? media.Deserialize<AniListMedia>(JsonOpts)
            : null;

    /// <summary>Null means the username doesn't exist.</summary>
    public async Task<IReadOnlyList<AniListListEntry>?> GetUserListAsync(string userName, CancellationToken ct = default)
    {
        if (await PostAsync(MediaListQuery, new { userName }, ct) is not { } data ||
            data.GetProperty("MediaListCollection") is not { ValueKind: JsonValueKind.Object } collection)
        {
            return null;
        }

        return [.. collection.GetProperty("lists").EnumerateArray()
            .SelectMany(list => list.GetProperty("entries").Deserialize<List<AniListListEntry>>(JsonOpts) ?? [])];
    }

    public async Task<IReadOnlyList<AniListMedia>> GetByMalIdsAsync(IReadOnlyList<int> malIds, CancellationToken ct = default) =>
        malIds.Count == 0 ? [] : PageMedia(await PostAsync(ByMalIdsQuery, new { ids = malIds }, ct));

    /// <summary>One GraphQL request; returns its <c>data</c> object, or null on 404 (AniList's
    /// answer for an unknown id or user) or when there is none. Any other failure throws <see cref="HttpRequestException"/>.</summary>
    private async Task<JsonElement?> PostAsync(string query, object variables, CancellationToken ct, JsonSerializerOptions? options = null)
    {
        using var response = await http.PostAsJsonAsync(Endpoint, new { query, variables }, options ?? JsonOpts, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object ? data.Clone() : null;
    }

    private static IReadOnlyList<AniListMedia> PageMedia(JsonElement? data) =>
        data?.GetProperty("Page").GetProperty("media").Deserialize<List<AniListMedia>>(JsonOpts) ?? [];

    /// <summary>
    /// One picture per genre (CatalogFilter.AllGenres) for the Categories tiles: the most popular
    /// anime in that genre not already used by an earlier genre (Action and Adventure share
    /// their top show), its banner if it has one (tiles are wide), else its cover. Every genre
    /// in one request (one aliased Page per genre), cached 24 h under one fixed key — the
    /// tiles barely change and AniList allows 90 req/min. A failed call caches an empty map
    /// for 5 min so an AniList outage isn't hit again on every Categories view; the page
    /// falls back to its gradients. Genre names only ever travel as GraphQL variables.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetGenreImagesAsync(CancellationToken ct = default)
    {
        const string key = "genre-images";
        if (cache.TryGetValue(key, out IReadOnlyDictionary<string, string>? hit) && hit is not null)
        {
            return hit;
        }

        var genres = CatalogFilter.AllGenres;
        var parameters = string.Join(", ", genres.Select((_, i) => $"$g{i}: String"));
        var pages = string.Join("\n", genres.Select((_, i) =>
            $"g{i}: Page(perPage: 10) {{ media(type: ANIME, isAdult: false, sort: POPULARITY_DESC, genre: $g{i}) {{ bannerImage coverImage {{ extraLarge }} }} }}"));
        var query = $"query ({parameters}) {{ {pages} }}";
        var variables = genres.Select((g, i) => (Key: $"g{i}", Value: g)).ToDictionary(x => x.Key, x => x.Value);

        var result = new Dictionary<string, string>();
        try
        {
            if (await PostAsync(query, variables, ct) is not { } data)
            {
                throw new HttpRequestException("AniList returned no data for the genre images.");
            }

            // Tolerant of partial results: a null/missing alias or media list just leaves that genre out.
            var used = new HashSet<string>();
            for (var i = 0; i < genres.Length; i++)
            {
                if (!data.TryGetProperty($"g{i}", out var page) || page.ValueKind != JsonValueKind.Object
                    || !page.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in media.EnumerateArray())
                {
                    // Rendered as a CSS background by the browser: https only, nothing else.
                    var url = HttpsUrl(item, "bannerImage")
                        ?? (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("coverImage", out var cover) ? HttpsUrl(cover, "extraLarge") : null);
                    if (url is not null && used.Add(url))
                    {
                        result[genres[i]] = url;
                        break;
                    }
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or JsonException)
        {
            cache.Set(key, (IReadOnlyDictionary<string, string>)result, TimeSpan.FromMinutes(5));
            return result;
        }

        cache.Set(key, (IReadOnlyDictionary<string, string>)result, TimeSpan.FromHours(24));
        return result;
    }

    private static string? HttpsUrl(JsonElement obj, string property) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(property, out var e) && e.ValueKind == JsonValueKind.String
        && e.GetString() is { } url && url.StartsWith("https://", StringComparison.Ordinal)
            ? url
            : null;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // AniList ignores most null filter variables but not all: an explicit `status: null`
    // matches nothing. Leaving unset filters out entirely is safe.
    private static readonly JsonSerializerOptions OmitNullsOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
