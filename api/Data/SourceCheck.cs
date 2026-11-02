namespace AniTether.Api.Data;

/// <summary>What a health check probed. One row per (source, kind) is kept current;
/// <see cref="Result"/> is one of "ok"/"broken"/"unknown" (see
/// <see cref="Features.Sources.SourceHealth"/>).</summary>
public static class SourceCheckKinds
{
    public const string Site = "site";
    public const string Search = "search";
    public const string Episode = "episode";
    public const string Embed = "embed";
    public const string Links = "links";
}

public class SourceCheck
{
    public int Id { get; set; }
    public Guid SourceId { get; set; }

    /// <summary>One of <see cref="SourceCheckKinds"/>.</summary>
    public required string Kind { get; set; }

    /// <summary>"ok", "broken" or "unknown". Unknown = a bot-block/challenge or a check
    /// that can't be decided from the outside (JS-rendered page, opaque id) — it never
    /// hides a source and never triggers an alert email.</summary>
    public required string Result { get; set; }

    public int? HttpStatus { get; set; }
    public string? Detail { get; set; }

    /// <summary>What to actually do about it, shown on the admin report page.</summary>
    public string? Hint { get; set; }

    public DateTimeOffset CheckedAt { get; set; }

    /// <summary>When the current unbroken-bad streak started (null while ok/unknown).</summary>
    public DateTimeOffset? FailingSince { get; set; }

    /// <summary>Consecutive broken runs. Unknown leaves it untouched; ok resets it. A
    /// source is hidden from users at <see cref="Features.Sources.SourceHealth.HideAfterFailures"/>.</summary>
    public int ConsecutiveFailures { get; set; }
}
