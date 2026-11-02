using AniTether.Api.Features.Sources;

namespace AniTether.Api.Features.Anime;

public record AnimeDetailResponse(Data.Anime Anime, IReadOnlyList<AnimeSourceLinkResponse> SourceLinks);
