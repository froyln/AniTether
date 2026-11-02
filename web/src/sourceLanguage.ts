// Values mirror api/Data/Languages.cs's whitelist.

export const LANGUAGE_LABELS: Record<string, string> = {
  en: 'English',
  es: 'Spanish',
}

// Values mirror api/Data/Audios.cs's whitelist. The in-page player can only switch form
// for a source whose template declares `{audio}`.
export const AUDIO_LABELS: Record<string, string> = {
  sub: 'Sub (subtitles)',
  dub: 'Dub (dubbed)',
}

/** No stored preference → subtitles. */
export function effectiveAudio(preferred: string | null): string {
  return preferred ?? 'sub'
}

/** No stored preference → the browser's own language, then English. */
export function effectiveLanguage(preferred: string | null): string {
  if (preferred) return preferred
  const nav = typeof navigator !== 'undefined' ? navigator.language : ''
  return nav.toLowerCase().startsWith('es') ? 'es' : 'en'
}

/** Reorders items so the preferred language's come first (list order within each group). */
export function orderByLanguage<T extends { language: string | null }>(items: T[], preferred: string): T[] {
  const matching = items.filter((i) => (i.language ?? 'en') === preferred)
  const rest = items.filter((i) => (i.language ?? 'en') !== preferred)
  return [...matching, ...rest]
}

/**
 * Source order for "which one first": preferred language first, and the last-used source
 * leads only its own language group — so watching one episode on a Spanish site never
 * outranks an English preference.
 */
export function orderSources<T extends { id: string; language: string | null }>(items: T[], preferred: string, lastId?: string | null): T[] {
  const last = items.filter((i) => i.id === lastId)
  return orderByLanguage([...last, ...items.filter((i) => i.id !== lastId)], preferred)
}

/** Groups items by language, preferred group first. A missing language falls into 'en'. */
export function groupByLanguage<T extends { language: string | null }>(
  items: T[],
  preferred: string,
): { language: string; items: T[] }[] {
  const languages = Array.from(new Set(items.map((i) => i.language ?? 'en')))
  languages.sort((a, b) => (a === preferred ? -1 : b === preferred ? 1 : 0))
  return languages.map((language) => ({ language, items: items.filter((i) => (i.language ?? 'en') === language) }))
}
