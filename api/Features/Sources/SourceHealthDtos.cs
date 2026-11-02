using AniTether.Api.Data;

namespace AniTether.Api.Features.Sources;

public record HealthCheckResponse(string Kind, string Result, int? HttpStatus, string? Detail, string? Hint, DateTimeOffset CheckedAt, DateTimeOffset? FailingSince, int ConsecutiveFailures)
{
    public static HealthCheckResponse From(SourceCheck c) => new(c.Kind, c.Result, c.HttpStatus, c.Detail, c.Hint, c.CheckedAt, c.FailingSince, c.ConsecutiveFailures);
}

public record HealthSourceResponse(Guid Id, string Name, string BaseUrl, bool Enabled, bool Healthy, IReadOnlyList<HealthCheckResponse> Checks);

public record UserReportResponse(int Id, string SourceName, int? AnilistId, string? AnimeTitle, int? Episode, string Url, DateTimeOffset CreatedAt);

public record HealthReportResponse(bool Running, DateTimeOffset? LastRunAt, IReadOnlyList<HealthSourceResponse> Sources, IReadOnlyList<UserReportResponse> Reports);
