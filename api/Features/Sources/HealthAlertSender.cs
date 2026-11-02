using System.Net;
using System.Net.Mail;
using System.Text;

namespace AniTether.Api.Features.Sources;

/// <summary>One source check that changed state, as it appears in the alert email and the
/// admin report.</summary>
public sealed record HealthAlertChange(string SourceName, string Kind, string Change, string Detail, string? Hint, bool Hidden);

/// <summary>
/// One email per run, only when something changed state. Plain <see cref="SmtpClient"/>
/// (no new dependency) and it is entirely optional: with no Smtp__Host configured the
/// run logs a warning and the report page still works.
/// </summary>
public class HealthAlertSender(IConfiguration config, ILogger<HealthAlertSender> log)
{
    public bool Configured => !string.IsNullOrWhiteSpace(config["Smtp:Host"]);

    public async Task SendAsync(IReadOnlyList<HealthAlertChange> changes, IReadOnlyList<string> recipients, CancellationToken ct)
    {
        if (changes.Count == 0)
        {
            return;
        }

        if (!Configured)
        {
            log.LogWarning("{Count} source check(s) changed state but no SMTP host is configured (Smtp__Host); not sending an email. {Summary}",
                changes.Count, string.Join("; ", changes.Select(c => $"{c.SourceName}/{c.Kind}: {c.Change}")));
            return;
        }

        if (recipients.Count == 0)
        {
            log.LogWarning("No Admin-role users found; not sending the source health email.");
            return;
        }

        // Compose passes unset variables as "", so treat empty like missing.
        var from = new[] { config["Smtp:From"], config["Smtp:User"] }.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "anitether@localhost";
        using var message = new MailMessage
        {
            From = new MailAddress(from),
            Subject = $"AniTether source health: {changes.Count} change(s) — {string.Join(", ", changes.Take(3).Select(c => $"{c.SourceName} {c.Change}"))}",
            Body = BuildBody(changes),
            IsBodyHtml = false,
        };

        foreach (var to in recipients)
        {
            message.To.Add(new MailAddress(to));
        }

        try
        {
            using var client = new SmtpClient(config["Smtp:Host"], int.TryParse(config["Smtp:Port"], out var port) ? port : 587)
            {
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
            };

            if (!string.IsNullOrWhiteSpace(config["Smtp:User"]))
            {
                client.Credentials = new NetworkCredential(config["Smtp:User"], config["Smtp:Password"]);
            }

            await client.SendMailAsync(message, ct);
            log.LogInformation("Sent the source health email to {Count} admin(s): {Summary}", recipients.Count, string.Join("; ", changes.Select(c => $"{c.SourceName}/{c.Kind}: {c.Change}")));
        }
        catch (Exception ex)
        {
            // Never let a mail failure fail a health run.
            log.LogError(ex, "Could not send the source health email.");
        }
    }

    private static string BuildBody(IReadOnlyList<HealthAlertChange> changes)
    {
        var body = new StringBuilder("AniTether's source health check found changes.\n\n");
        foreach (var change in changes)
        {
            body.AppendLine($"- {change.SourceName} ({change.Kind}): {change.Change}");
            body.AppendLine($"    {change.Detail}");
            if (change.Hidden)
            {
                body.AppendLine("    Its watch buttons are now hidden from users until a check passes.");
            }

            if (!string.IsNullOrWhiteSpace(change.Hint))
            {
                body.AppendLine($"    Fix: {change.Hint}");
            }

            body.AppendLine();
        }

        body.AppendLine("Open the admin Sources health report to see the full picture.");
        return body.ToString();
    }
}
