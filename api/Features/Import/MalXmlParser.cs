using System.Xml;
using System.Xml.Linq;
using AniTether.Api.Data;

namespace AniTether.Api.Features.Import;

public record MalListItem(int MalId, int WatchedEpisodes, ListStatus Status, int? Score,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);

/// <summary>Parses a MyAnimeList list-export XML (Profile → Export list).</summary>
public static class MalXmlParser
{
    public static IReadOnlyList<MalListItem> Parse(Stream xml)
    {
        // Untrusted file upload — disable DTD/external entity resolution (XXE).
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(xml, settings);
        var doc = XDocument.Load(reader);

        var items = new List<MalListItem>();
        foreach (var node in doc.Root?.Elements("anime") ?? Enumerable.Empty<XElement>())
        {
            if ((int?)node.Element("series_animedb_id") is not int malId) continue;

            items.Add(new MalListItem(
                malId,
                (int?)node.Element("my_watched_episodes") ?? 0,
                MapStatus(node.Element("my_status")?.Value, node.Element("my_rewatching")?.Value),
                NullIfUnrated((int?)node.Element("my_score")),
                ParseDate(node.Element("my_start_date")?.Value),
                ParseDate(node.Element("my_finish_date")?.Value)));
        }

        return items;
    }

    private static ListStatus MapStatus(string? status, string? rewatching) => (status, rewatching) switch
    {
        (_, "1") => ListStatus.Rewatching,
        ("Watching", _) => ListStatus.Watching,
        ("Completed", _) => ListStatus.Completed,
        ("On-Hold", _) => ListStatus.OnHold,
        ("Dropped", _) => ListStatus.Dropped,
        _ => ListStatus.PlanToWatch,
    };

    private static int? NullIfUnrated(int? score) => score is null or 0 ? null : score;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateOnly.TryParse(value, out var date) ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
}
