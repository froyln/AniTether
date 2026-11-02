namespace AniTether.Api.Features.Anime;

public static class AnimeAiring
{
    public static bool HasAired(AniTether.Api.Data.Anime anime, int episode) =>
        anime.AiringStatus != "NOT_YET_RELEASED" &&
        !(anime.AiringStatus == "RELEASING" && anime.NextAiringEpisode is int next && episode >= next);
}
