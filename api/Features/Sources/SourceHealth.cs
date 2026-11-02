using AniTether.Api.Data;
using System.Text.RegularExpressions;

namespace AniTether.Api.Features.Sources;

/// <summary>One check's verdict, before it is persisted or shown.</summary>
public sealed record CheckOutcome(string Result, int? HttpStatus, string Detail, string? Hint = null)
{
    public const string Ok = "ok";
    public const string Broken = "broken";
    public const string Unknown = "unknown";
}

/// <summary>
/// How a probe's raw HTTP facts map to ok / broken / unknown, and which alert (if any)
/// a state change earns. Pure functions, no I/O — the point is that every rule the
/// admin report trusts is covered by a test that can't be broken by a site's behaviour
/// changing.
/// </summary>
public static class SourceHealth
{
    /// <summary>Consecutive broken runs before a source's watch buttons are hidden from
    /// users. Unknown never counts (no false auto-hides on bot-blocked responses).</summary>
    public const int HideAfterFailures = 3;

    /// <summary>Cloudflare-style protections answer a non-browser client a 404
    /// "Content Removed" page for a URL that plays fine in a real browser. Without this,
    /// one such response reads as "URL format changed" and, three runs later, hides a
    /// working source.</summary>
    private static readonly Regex BlockedPagePattern = new("Content Removed|cf-browser-verification|Just a moment", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool LooksBlocked(string bodyPrefix) => BlockedPagePattern.IsMatch(bodyPrefix);

    /// <summary>The source's own homepage. 5xx/connection failure = down. A redirect to
    /// another host is the domain-moved case AGENTS.md warns about, and the one fix the
    /// report page can offer in one click.</summary>
    public static CheckOutcome Site(int? status, bool blocked, string? movedTo)
    {
        if (blocked)
        {
            return new(CheckOutcome.Unknown, status, "Bot-blocked or challenge page, not a real answer.", "Check by hand in a browser.");
        }

        if (movedTo is not null)
        {
            return new(CheckOutcome.Broken, status, $"Redirects to a different host: {movedTo}.", $"The domain moved. Replace {movedTo}'s old host in the base URL and every template.");
        }

        return status switch
        {
            null => new(CheckOutcome.Broken, null, "No response (connection failed or timed out).", "The site is down or unreachable from here. Wait; disable the source if it lasts."),
            >= 200 and < 400 => new(CheckOutcome.Ok, status, "Site reachable."),
            >= 500 => new(CheckOutcome.Broken, status, $"Server error {status}.", "The site is down. Wait; disable the source if it lasts."),
            _ => new(CheckOutcome.Broken, status, $"Unexpected status {status}.", "The URL may be wrong. Check by hand in a browser."),
        };
    }

    /// <summary>A page under the source's own host that a real user would open. 404 is
    /// the "URL format changed" signal; 403/503 are challenge codes and stay unknown.
    /// A redirect is fine unless it leaves the host — same-host canonicalization is
    /// everyday, off-host is the domain-moved death case AGENTS.md warns about.</summary>
    public static CheckOutcome Page(int? status, bool blocked, string? movedTo, string okDetail, string notFoundHint)
    {
        if (blocked)
        {
            return new(CheckOutcome.Unknown, status, "Bot-blocked or challenge page, not a real answer.", "Check by hand in a browser.");
        }

        if (movedTo is not null)
        {
            return new(CheckOutcome.Broken, status, $"Redirects to a different host: {movedTo}.", $"The domain moved. Replace {movedTo}'s old host in the base URL and every template.");
        }

        return status switch
        {
            null => new(CheckOutcome.Broken, null, "No response (connection failed or timed out).", "The site is down or unreachable from here."),
            >= 200 and < 300 => new(CheckOutcome.Ok, status, okDetail),
            404 => new(CheckOutcome.Broken, status, "404 — the URL format changed.", notFoundHint),
            403 or 429 or 503 => new(CheckOutcome.Unknown, status, $"Status {status} — usually a bot check or rate limit.", "Check by hand in a browser."),
            >= 400 => new(CheckOutcome.Broken, status, $"Unexpected status {status}.", "The URL may be wrong. Check by hand in a browser."),
            _ => new(CheckOutcome.Ok, status, okDetail),
        };
    }

    /// <summary>Only the framing headers decide this one, and only a blocking header is
    /// "broken". A 404 here is ambiguous (a block page and a dead template look the
    /// same), so it stays unknown — see <see cref="LooksBlocked"/>.</summary>
    public static CheckOutcome Embed(int? status, bool blocked, string? frameOptions, string? frameAncestors)
    {
        if (blocked)
        {
            return new(CheckOutcome.Unknown, status, "Bot-blocked or challenge page, not a real answer.", "Check by hand in a browser.");
        }

        if (frameOptions is { Length: > 0 })
        {
            return new(CheckOutcome.Broken, status, $"X-Frame-Options: {frameOptions} — this page refuses to be framed.", "Turn off the embed template for this source and send users to the site itself.");
        }

        if (frameAncestors is { Length: > 0 })
        {
            return new(CheckOutcome.Broken, status, $"CSP frame-ancestors: {frameAncestors} — this page refuses to be framed.", "Turn off the embed template for this source and send users to the site itself.");
        }

        if (status is null)
        {
            return new(CheckOutcome.Unknown, null, "No response — can't tell a dead embed from a block.", "Check by hand in a browser.");
        }

        if (status is >= 200 and < 300)
        {
            return new(CheckOutcome.Ok, status, "Answers and doesn't block framing. This can't confirm the video actually plays.");
        }

        return new(CheckOutcome.Unknown, status, $"Status {status} — can't tell a dead embed from a bot block.", "Check by hand in a browser.");
    }

    /// <summary>What an alert email should say about one check, or null to stay quiet.
    /// Only real state changes are worth an email: a first run that finds a source
    /// already broken is news, and so is a recovery. Unknown never emails.</summary>
    public static string? AlertFor(string? previousResult, CheckOutcome outcome, int consecutiveFailures)
    {
        if (outcome.Result == CheckOutcome.Broken)
        {
            if (previousResult is null)
            {
                return "broken";
            }

            return previousResult == CheckOutcome.Broken ? null : "broken";
        }

        return previousResult == CheckOutcome.Broken && outcome.Result == CheckOutcome.Ok ? "recovered" : null;
    }

    /// <summary>Applied to a stored row: a broken run counts up, ok resets, unknown
    /// changes nothing either way (so an intermittently blocked source neither hides
    /// itself nor un-hides itself).</summary>
    public static void ApplyOutcome(SourceCheck check, CheckOutcome outcome, DateTimeOffset now)
    {
        switch (outcome.Result)
        {
            case CheckOutcome.Broken:
                if (check.ConsecutiveFailures == 0)
                {
                    check.FailingSince = now;
                }

                check.ConsecutiveFailures++;
                break;
            case CheckOutcome.Ok:
                check.ConsecutiveFailures = 0;
                check.FailingSince = null;
                break;
        }

        check.Result = outcome.Result;
        check.HttpStatus = outcome.HttpStatus;
        check.Detail = outcome.Detail;
        check.Hint = outcome.Hint;
        check.CheckedAt = now;
    }

    /// <summary>A source is hidden from users once any check that a watch button depends
    /// on (site / episode / embed) has failed this many times in a row.</summary>
    public static bool IsHidden(SourceCheck check) =>
        check.Result == CheckOutcome.Broken &&
        check.ConsecutiveFailures >= HideAfterFailures &&
        check.Kind is Data.SourceCheckKinds.Site or Data.SourceCheckKinds.Episode or Data.SourceCheckKinds.Embed;
}
