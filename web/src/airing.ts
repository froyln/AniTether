import type { Anime } from './api'

export function lastAired(anime: Anime | null | undefined): number {
  if (anime?.airingStatus === 'NOT_YET_RELEASED') return 0
  if (anime?.airingStatus === 'RELEASING' && anime.nextAiringEpisode) return anime.nextAiringEpisode - 1
  return Infinity
}
