import type { Source, SourceLink } from './api'

// A template without {slug}/{mal_id} (e.g. /watch/{anilist_id}?ep=) needs no saved link.
// {mal_id} needs the anime's MAL id (from AniList, not every anime has one).
export const canWatch = (template: string | null, link: SourceLink | undefined, malId: number | null): template is string =>
  !!template && (template.includes('{slug}') ? !!link : template.includes('{mal_id}') ? malId != null : true)

/**
 * Whether `resolveWatchUrl` will produce a real deep link for this source — either by
 * filling its template (`canWatch`) or, when that's not possible, via a saved link's own
 * URL (the same fallback `resolveWatchUrl` uses). False only when it would fall back to
 * a title search. Use this (not `canWatch` alone) to decide whether to render a ▶ button.
 */
export const hasWatchTarget = (source: Source, link: SourceLink | undefined, malId: number | null): boolean =>
  canWatch(source.episodeUrlTemplate, link, malId) || !!link

export const linkFor = (links: SourceLink[], source: Source): SourceLink | undefined =>
  links.find((link) => link.sourceId === source.id)

// slug comes from the server (SourceUrlValidation.DeriveSlug), which knows the site's URL shape
// audio is the user's playback-form preference: 'sub' or 'dub'. A template with no {audio}
// placeholder has only one form — the string is passed anyway and simply never substituted.
function fillEpisodeTemplate(
  template: string,
  link: SourceLink | undefined,
  anilistId: number,
  malId: number | null,
  episode: number,
  audio: string,
): string {
  return template
    .replace('{slug}', encodeURIComponent(link?.slug ?? ''))
    .replace('{anilist_id}', String(anilistId))
    .replace('{mal_id}', String(malId ?? ''))
    .replace('{episode}', String(episode))
    .replace('{audio}', audio)
}

// Whether this source's templates declare a {audio} placeholder — the only signal that
// it has both forms. The watch page shows a Sub/Dub control only when the active
// embed template does.
export const canSwitchAudio = (template: string | null): boolean => !!template?.includes('{audio}')

/**
 * Resolves where "Watch episode N on {source}" should open: the episode template
 * filled in (when the source has one and it can be filled), else the anime's own page
 * on that source, else a title search. Callers that want to know whether this is a real
 * deep link (vs. this search fallback) should check `canWatch` first.
 */
export function resolveWatchUrl(
  source: Source,
  link: SourceLink | undefined,
  anilistId: number,
  malId: number | null,
  title: string,
  episode: number,
  audio: string,
): string {
  if (canWatch(source.episodeUrlTemplate, link, malId)) return fillEpisodeTemplate(source.episodeUrlTemplate, link, anilistId, malId, episode, audio)
  if (link) return link.url
  return source.searchUrlTemplate.replace('{title}', encodeURIComponent(title))
}

// Whether resolveEmbedUrl will produce a real iframe URL for this source. No
// fallback to a saved link's own URL — an embed always needs its own real template.
export const canEmbed = (source: Source, link: SourceLink | undefined, malId: number | null): boolean =>
  canWatch(source.embedUrlTemplate, link, malId)

// Whether the source's own episode page can be framed in-place. Unlike canEmbed, the target is the episode
// template itself, so the fill rule is canWatch's. Per-source opt-in flag, never inferred.
export const canPageEmbed = (source: Source, link: SourceLink | undefined, malId: number | null): boolean =>
  source.allowPageEmbed && canWatch(source.episodeUrlTemplate, link, malId)

/** The URL to put in the watch page's iframe, or null when this source can't be embedded. */
export function resolveEmbedUrl(
  source: Source,
  link: SourceLink | undefined,
  anilistId: number,
  malId: number | null,
  episode: number,
  audio: string,
): string | null {
  if (!canEmbed(source, link, malId)) return null
  return fillEpisodeTemplate(source.embedUrlTemplate!, link, anilistId, malId, episode, audio)
}

// Whether AniTether's own watch page can show this source in its iframe — either rule.
// Every "▶ Continue" button uses this to pick the in-site player over a new tab.
export const canFrame = (source: Source, link: SourceLink | undefined, malId: number | null): boolean =>
  canEmbed(source, link, malId) || canPageEmbed(source, link, malId)
