// Values mirror api/Features/Anime/CatalogFilter.cs whitelists.

// AniList's GenreCollection (fixed set; Hentai left out, catalog is isAdult: false).
// Mirrored by CatalogFilter.AllGenres server-side (Categories tile images) — change both.
export const GENRES = [
  'Action',
  'Adventure',
  'Comedy',
  'Drama',
  'Ecchi',
  'Fantasy',
  'Horror',
  'Mahou Shoujo',
  'Mecha',
  'Music',
  'Mystery',
  'Psychological',
  'Romance',
  'Sci-Fi',
  'Slice of Life',
  'Sports',
  'Supernatural',
  'Thriller',
]

export const FORMATS: Record<string, string> = {
  TV: 'TV',
  TV_SHORT: 'TV Short',
  MOVIE: 'Movie',
  OVA: 'OVA',
  ONA: 'ONA',
  SPECIAL: 'Special',
  MUSIC: 'Music',
}

export const SEASONS: Record<string, string> = { WINTER: 'Winter', SPRING: 'Spring', SUMMER: 'Summer', FALL: 'Fall' }

export const AIRING_STATUSES: Record<string, string> = {
  RELEASING: 'Airing',
  FINISHED: 'Finished',
  NOT_YET_RELEASED: 'Upcoming',
  CANCELLED: 'Cancelled',
  HIATUS: 'Hiatus',
}

export const SORTS: Record<string, string> = {
  relevance: 'Best match',
  trending: 'Trending',
  popular: 'Most popular',
  score: 'Top rated',
  newest: 'Newest',
}

export const FILTER_KEYS = ['q', 'genres', 'format', 'year', 'season', 'status', 'sort'] as const

const SEASON_ORDER = ['WINTER', 'SPRING', 'SUMMER', 'FALL']

/** AniList seasons: Winter = Jan–Mar, Spring = Apr–Jun, Summer = Jul–Sep, Fall = Oct–Dec. */
export function seasonAt(date: Date, offset = 0) {
  const index = Math.floor(date.getMonth() / 3) + offset
  const year = date.getFullYear() + Math.floor(index / 4)
  return { season: SEASON_ORDER[((index % 4) + 4) % 4], year }
}
