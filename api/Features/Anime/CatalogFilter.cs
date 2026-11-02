using System.Text.RegularExpressions;

namespace AniTether.Api.Features.Anime;

/// <summary>
/// Validated catalog filters. Every value ends up as a GraphQL variable (never spliced
/// into the query text), but enums are still whitelisted so bad input is a clean 400
/// instead of an AniList error surfacing as a 500.
/// </summary>
public partial record CatalogFilter(
    string? Search,
    IReadOnlyList<string>? Genres,
    string? Format,
    int? Year,
    string? Season,
    string? Status,
    string Sort)
{
    private static readonly Dictionary<string, string> Sorts = new()
    {
        ["trending"] = "TRENDING_DESC",
        ["popular"] = "POPULARITY_DESC",
        ["score"] = "SCORE_DESC",
        ["newest"] = "START_DATE_DESC",
        ["relevance"] = "SEARCH_MATCH",
    };

    private static readonly HashSet<string> Formats = ["TV", "TV_SHORT", "MOVIE", "SPECIAL", "OVA", "ONA", "MUSIC"];
    private static readonly HashSet<string> Seasons = ["WINTER", "SPRING", "SUMMER", "FALL"];
    private static readonly HashSet<string> Statuses = ["RELEASING", "FINISHED", "NOT_YET_RELEASED", "CANCELLED", "HIATUS"];

    private const int MaxGenres = 5;

    [GeneratedRegex(@"^[A-Za-z][A-Za-z \-]{0,29}$")]
    private static partial Regex GenrePattern();

    // AniList's GenreCollection minus Hentai. Mirrors web/src/catalogFilters.ts GENRES; only
    // the Categories tile images use it server-side (a fixed list = one fixed cache entry).
    public static readonly string[] AllGenres =
    [
        "Action", "Adventure", "Comedy", "Drama", "Ecchi", "Fantasy", "Horror", "Mahou Shoujo", "Mecha", "Music",
        "Mystery", "Psychological", "Romance", "Sci-Fi", "Slice of Life", "Sports", "Supernatural", "Thriller",
    ];

    public static (CatalogFilter? Filter, string? Error) Parse(
        string? q, string? genres, string? format, int? year, string? season, string? status, string? sort)
    {
        var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (search is { Length: > 100 })
        {
            return (null, "Search text too long.");
        }

        List<string>? genreList = null;
        if (!string.IsNullOrWhiteSpace(genres))
        {
            genreList = genres.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
            if (genreList.Count > MaxGenres || genreList.Any(g => !GenrePattern().IsMatch(g)))
            {
                return (null, $"Up to {MaxGenres} genres, letters only.");
            }
        }

        if (format is not null && !Formats.Contains(format))
        {
            return (null, "Unknown format.");
        }

        if (season is not null && !Seasons.Contains(season))
        {
            return (null, "Unknown season.");
        }

        if (status is not null && !Statuses.Contains(status))
        {
            return (null, "Unknown status.");
        }

        if (year is int y && (y < 1940 || y > DateTime.UtcNow.Year + 2))
        {
            return (null, "Year out of range.");
        }

        // relevance only means something with search text; default follows the same rule
        var sortKey = sort ?? (search is null ? "trending" : "relevance");
        if (!Sorts.TryGetValue(sortKey, out var aniListSort))
        {
            return (null, "Unknown sort.");
        }

        if (search is null && aniListSort == "SEARCH_MATCH")
        {
            aniListSort = Sorts["trending"];
        }

        return (new CatalogFilter(search, genreList, format, year, season, status, aniListSort), null);
    }
}
