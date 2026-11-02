async function request<T>(path: string, init?: RequestInit, notFoundAsNull = false): Promise<T> {
  const res = await fetch(`/api${path}`, {
    credentials: 'include',
    headers: init?.body instanceof FormData ? undefined : { 'Content-Type': 'application/json' },
    ...init,
  })

  if (notFoundAsNull && res.status === 404) return null as T
  if (!res.ok) {
    const body = await res.json().catch(() => null)
    throw new Error(body?.error ?? `Request failed: ${res.status}`)
  }

  if (res.status === 204) return undefined as T
  return res.json()
}

export type User = { id: string; email: string; displayName: string; isAdmin: boolean; canManageUsers: boolean; preferredLanguage: string | null; preferredAudio: string | null; theme: string | null; colorMode: string | null; profilePublic: boolean; profileBio: string | null; avatarUpdatedAt: string | null }

/** PATCH /api/me/preferences replaces all four, so callers send the user's current values
 * with their one change on top. */
export type Preferences = Pick<User, 'preferredLanguage' | 'preferredAudio' | 'theme' | 'colorMode'>

export type Source = {
  id: string
  name: string
  baseUrl: string
  searchUrlTemplate: string
  episodeUrlTemplate: string | null
  animePageUrlTemplate: string | null
  language: string | null
  embedUrlTemplate: string | null
  sandboxed: boolean
  allowPageEmbed: boolean
  enabled: boolean
  healthy: boolean
}

export type HealthCheck = {
  kind: 'site' | 'search' | 'episode' | 'embed' | 'links'
  result: 'ok' | 'broken' | 'unknown'
  httpStatus: number | null
  detail: string | null
  hint: string | null
  checkedAt: string
  failingSince: string | null
  consecutiveFailures: number
}

export type HealthSource = {
  id: string
  name: string
  baseUrl: string
  enabled: boolean
  healthy: boolean
  checks: HealthCheck[]
}

export type HealthReport = {
  running: boolean
  lastRunAt: string | null
  sources: HealthSource[]
  reports: { id: number; sourceName: string; anilistId: number | null; animeTitle: string | null; episode: number | null; url: string; createdAt: string }[]
}

/** Null role = regular user. */
export type Role = 'Admin' | 'Manager'
export type AdminUser = { id: string; email: string; displayName: string; role: Role | null; locked: boolean }

export type SourceLink = { sourceId: string; url: string; slug: string }

export type ImportSummary = { added: number; updated: number; skipped: number; unmatched: number }

export type Anime = {
  anilistId: number
  titleRomaji: string
  titleEnglish: string | null
  malId: number | null
  coverUrl: string | null
  bannerUrl: string | null
  description: string | null
  genres: string[]
  episodes: number | null
  format: string | null
  airingStatus: string | null
  nextAiringEpisode: number | null
}

export type AnimeDetail = { anime: Anime; sourceLinks: SourceLink[] }

export type ListStatus = 'PlanToWatch' | 'Watching' | 'Completed' | 'OnHold' | 'Dropped' | 'Rewatching'

export type ListEntry = {
  anilistId: number
  titleRomaji: string
  titleEnglish: string | null
  malId: number | null
  coverUrl: string | null
  episodes: number | null
  status: ListStatus
  progress: number
  score: number | null
  notes: string | null
  lastSourceId: string | null
  updatedAt: string
  revision: string
  sourceLinks: SourceLink[]
}

export const api = {
  me: () => request<User>('/me'),
  login: (email: string, password: string) =>
    request<User>('/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }),
  logout: () => request<void>('/auth/logout', { method: 'POST' }),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<void>('/me/password', { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) }),
  listUsers: () => request<AdminUser[]>('/admin/users'),
  createUser: (user: { email: string; displayName: string; password: string; role: Role | null }) =>
    request<AdminUser>('/admin/users', { method: 'POST', body: JSON.stringify(user) }),
  updateUser: (id: string, patch: { displayName?: string; setRole?: boolean; role?: Role | null; locked?: boolean }) =>
    request<AdminUser>(`/admin/users/${id}`, { method: 'PATCH', body: JSON.stringify(patch) }),
  resetUserPassword: (id: string, password: string) =>
    request<void>(`/admin/users/${id}/password`, { method: 'POST', body: JSON.stringify({ password }) }),
  updatePreferences: (user: User, change: Partial<Preferences>) => {
    const { preferredLanguage, preferredAudio, theme, colorMode } = { ...user, ...change }
    return request<User>('/me/preferences', { method: 'PATCH', body: JSON.stringify({ preferredLanguage, preferredAudio, theme, colorMode }) })
  },
  updateProfile: (profile: { displayName: string; bio: string; profilePublic: boolean }) =>
    request<void>('/me/profile', { method: 'PATCH', body: JSON.stringify(profile) }),
  uploadAvatar: (contentType: string, data: string) => request<void>('/me/avatar', { method: 'PUT', body: JSON.stringify({ contentType, data }) }),
  removeAvatar: () => request<void>('/me/avatar', { method: 'DELETE' }),
  myProfilePreview: () => request<PublicProfile>('/me/profile-preview'),
  publicProfile: (id: string) => request<PublicProfile>(`/profiles/${id}`, undefined, true),
  searchAnime: (q: string) => request<Anime[]>(`/anime/search?q=${encodeURIComponent(q)}`),
  browseCatalog: (filters: Record<string, string>, page: number) =>
    request<Anime[]>(`/catalog?${new URLSearchParams({ ...filters, page: String(page) })}`),
  getAnimeDetail: (anilistId: number) => request<AnimeDetail>(`/anime/${anilistId}`),
  resolveLinks: (anilistId: number) => request<AnimeDetail>(`/anime/${anilistId}/resolve-links`, { method: 'POST' }),
  getList: () => request<ListEntry[]>('/list'),
  getEntry: (anilistId: number) => request<ListEntry | null>(`/list/${anilistId}`, undefined, true),
  addToList: (anilistId: number) => request<ListEntry>('/list', { method: 'POST', body: JSON.stringify({ anilistId }) }),
  updateEntry: (
    anilistId: number,
    patch: Partial<{ status: ListStatus; progress: number; score: number; notes: string; lastSourceId: string }>,
  ) => request<ListEntry>(`/list/${anilistId}`, { method: 'PATCH', body: JSON.stringify(patch) }),
  applyProgress: (anilistId: number, episode: number, lastSourceId?: string, observedRevision?: string) =>
    request<ListEntry>(`/list/${anilistId}/apply-progress`, { method: 'POST', body: JSON.stringify({ episode, ...(lastSourceId ? { lastSourceId } : {}), ...(observedRevision ? { observedRevision } : {}) }) }),
  removeFromList: (anilistId: number) => request<void>(`/list/${anilistId}`, { method: 'DELETE' }),
  getSources: () => request<Source[]>('/sources'),
  getWatchSources: () => request<Source[]>('/sources'),
  getGenreImages: () => request<Record<string, string>>('/catalog/genre-images'),
  getAllSources: () => request<Source[]>('/sources/all'),
  getSourceHealth: () => request<HealthReport>('/admin/sources/health'),
  checkSources: () => request<{ started: boolean }>('/admin/sources/check', { method: 'POST' }),
  checkOneSource: (id: string) => request<HealthCheck[]>(`/admin/sources/check/${id}`, { method: 'POST' }),
  reportBroken: (sourceId: string, report: { anilistId: number; episode: number; url: string }) =>
    request<void>(`/sources/${sourceId}/report`, { method: 'POST', body: JSON.stringify(report) }),
  createSource: (source: Omit<Source, 'id' | 'healthy'>) =>
    request<Source>('/sources', { method: 'POST', body: JSON.stringify(source) }),
  updateSource: (id: string, patch: Partial<Omit<Source, 'id' | 'healthy'>>) =>
    request<Source>(`/sources/${id}`, { method: 'PATCH', body: JSON.stringify(patch) }),
  setSourceLink: (sourceId: string, anilistId: number, url: string) =>
    request<SourceLink>(`/sources/${sourceId}/links`, { method: 'POST', body: JSON.stringify({ anilistId, url }) }),
  importAniList: (username: string) =>
    request<ImportSummary>('/import/anilist', { method: 'POST', body: JSON.stringify({ username }) }),
  importMal: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return request<ImportSummary>('/import/mal', { method: 'POST', body: form })
  },
}

export type PublicEntry = { anilistId: number; title: string; coverUrl: string | null; status: ListStatus; progress: number; score: number | null; updatedAt: string }
export type PublicProfile = { id: string; displayName: string; bio: string | null; avatarUpdatedAt: number | null; entries: PublicEntry[]; isPreview: boolean; isPublic: boolean }
