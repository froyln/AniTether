using System.Net.Http.Headers;
using System.Text;

namespace AniTether.Api.Features.Sources;

/// <summary>A source gave no real answer: network error, timeout, 5xx or 429. Callers store
/// nothing, so the next request retries instead of a transient failure becoming a 7-day miss.</summary>
public class SourceUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record SourceHttpResponse(int Status, HttpResponseHeaders Headers, string? MediaType, string Body);

/// <summary>Every GET to a source goes through here. Timeout and no-redirect come from the
/// HttpClient registrations in Program.cs; this adds the User-Agent and the body cap.</summary>
public static class SourceHttp
{
    public const int MaxBodyBytes = 1_000_000;

    /// <summary>Any HTTP status is an answer. Throws <see cref="SourceUnavailableException"/>
    /// only when there is none; a cancelled <paramref name="ct"/> still throws as cancellation.</summary>
    public static async Task<SourceHttpResponse> GetAsync(HttpClient http, HttpRequestMessage request, int maxBytes, CancellationToken ct)
    {
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AniTetherBot", "1.0"));
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await ReadCappedAsync(response.Content, maxBytes, ct);
            return new((int)response.StatusCode, response.Headers, response.Content.Headers.ContentType?.MediaType, body);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            throw new SourceUnavailableException($"No answer from {request.RequestUri?.Host}.", ex);
        }
    }

    /// <summary>The body of a 2xx whose media type <paramref name="accept"/> takes; null for any
    /// other real answer (404, redirect, wrong type). 5xx and 429 count as no answer.</summary>
    public static async Task<string?> GetBodyAsync(HttpClient http, HttpRequestMessage request, Func<string?, bool> accept, CancellationToken ct)
    {
        var response = await GetAsync(http, request, MaxBodyBytes, ct);
        if (response.Status is >= 500 or 429)
        {
            throw new SourceUnavailableException($"HTTP {response.Status} from {request.RequestUri?.Host}.");
        }

        return response.Status is >= 200 and < 300 && accept(response.MediaType) ? response.Body : null;
    }

    private static async Task<string> ReadCappedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var capped = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while (capped.Length < maxBytes && (read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            capped.Write(buffer, 0, (int)Math.Min(read, maxBytes - capped.Length));
        }

        return Encoding.UTF8.GetString(capped.ToArray());
    }
}
