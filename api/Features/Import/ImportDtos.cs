namespace AniTether.Api.Features.Import;

public record ImportAniListRequest(string Username);

public record ImportSummary(int Added, int Updated, int Skipped, int Unmatched);
