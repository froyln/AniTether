import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { api, type AnimeDetail as AnimeDetailData, type ListEntry, type Source } from '../api'
import { lastAired } from '../airing'
import { useAuth } from '../AuthContext'
import { groupByLanguage, AUDIO_LABELS, LANGUAGE_LABELS, orderSources } from '../sourceLanguage'
import { canEmbed, canFrame, canSwitchAudio, linkFor, resolveEmbedUrl, resolveWatchUrl } from '../watchUrl'
import './AnimeWatch.css'

// "Played long enough, mark it watched automatically" fallback for players we can't
// read progress from directly.
const AUTO_MARK_MS = 5 * 60 * 1000

export function AnimeWatch() {
  const { anilistId, episode: episodeParam } = useParams<{ anilistId: string; episode: string }>()
  const id = Number(anilistId)
  const episode = Number(episodeParam)
  const navigate = useNavigate()
  const { language, audio, user, refresh } = useAuth()
  // ?source=<id> (from a card's or dialog's ▶ on a specific site, or Prev/Next) opens the
  // player on that site, not this page's own default. Ignored when it can't be framed.
  const [params] = useSearchParams()

  const [detail, setDetail] = useState<AnimeDetailData | null>(null)
  const [sources, setSources] = useState<Source[]>([])
  // undefined = list still loading, null = not in the list
  const [entry, setEntry] = useState<ListEntry | null | undefined>(undefined)
  const [sourceId, setSourceId] = useState<string | null>(null)
  const [marked, setMarked] = useState(false)
  const [reported, setReported] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const loadEntry = () => api.getEntry(id).then(setEntry)

  useEffect(() => {
    let stale = false
    setDetail(null)
    setEntry(undefined)
    setSourceId(null)
    setError(null)
    api
      .getAnimeDetail(id)
      .then((value) => { if (!stale) setDetail(value) })
      .catch(() => { if (!stale) setError('Could not load this anime.') })
    api.getWatchSources().then((value) => { if (!stale) setSources(value) })
      .catch(() => { if (!stale) setError('Could not load sources.') })
    api.getEntry(id).then((value) => { if (!stale) setEntry(value) })
      .catch(() => { if (!stale) setError('Could not load your list entry.') })
    return () => { stale = true }
  }, [id])

  useEffect(() => {
    setMarked(false)
    setReported(false)
  }, [id, episode])

  // Computed with detail possibly still null (loading) so every hook below stays above
  // the "still loading" early return — React requires the same hooks every render.
  const anime = detail?.anime
  const sourceLinks = detail?.sourceLinks ?? []
  const link = (source: Source) => linkFor(sourceLinks, source)
  // A source is frameable when it has an embed provider URL or, when opted in
  // via allowPageEmbed, its own episode page frames cleanly too (that page's
  // URL is the episode template itself, so canPageEmbed uses canWatch's rule).
  const embeddable = anime ? sources.filter((s) => canFrame(s, link(s), anime.malId)) : []
  const defaultSource = orderSources(embeddable, language, entry?.lastSourceId)[0]
  const requested = embeddable.find((s) => s.id === (sourceId ?? params.get('source')))
  const activeSource = requested ?? defaultSource
  const frameUrl =
    activeSource && anime
      ? canEmbed(activeSource, link(activeSource), anime.malId)
        ? resolveEmbedUrl(activeSource, link(activeSource), id, anime.malId, episode, audio)
        : activeSource.allowPageEmbed
          ? resolveWatchUrl(activeSource, link(activeSource), id, anime.malId, anime.titleEnglish ?? anime.titleRomaji, episode, audio)
          : null
      : null

  // Same rule as the anime page: nothing to play before an episode has aired anywhere.
  const airedThrough = lastAired(anime)
  const notAired = episode > airedThrough

  // Not in the list yet? Add it first, so the button always works.
  const markWatched = async () => {
    if (marked) return
    setMarked(true)
    try {
      if (!entry) await api.addToList(id)
      await api.updateEntry(id, { progress: Math.max(entry?.progress ?? 0, episode), ...(activeSource ? { lastSourceId: activeSource.id } : {}) })
      await loadEntry()
    } catch {
      setMarked(false)
      setError('Could not update your list.')
      loadEntry().catch(() => setError('Could not reload your list entry.'))
    }
  }

  const autoMarkWatched = async () => {
    if (!entry || marked) return
    setMarked(true)
    try {
      await api.applyProgress(id, episode, activeSource?.id, entry.revision)
      await loadEntry()
    } catch {
      setMarked(false)
      setError('Could not update your list.')
      loadEntry().catch(() => setError('Could not reload your list entry.'))
    }
  }

  // Auto-mark after AUTO_MARK_MS on the page. Reschedules on a source switch too, so a
  // stale timer can't record lastSourceId for whatever source was active when it was
  // first set rather than the current one.
  useEffect(() => {
    if (!entry || marked || entry.progress >= episode || notAired) return
    const timer = setTimeout(autoMarkWatched, AUTO_MARK_MS)
    return () => clearTimeout(timer)
  }, [entry, marked, notAired, episode, activeSource?.id])

  // The automatic health checks can't see a dead player (they only read status and
  // headers), so this is how a user tells the admins that one is broken.
  const reportBroken = async () => {
    if (!activeSource || !frameUrl || reported) return
    setReported(true)
    try {
      await api.reportBroken(activeSource.id, { anilistId: id, episode, url: frameUrl })
    } catch {
      setReported(false)
      setError('Could not send that report.')
    }
  }

  // Sub/Dub is the user's stored preference (context), so the next anime and the
  // "Open on site" link agree with the player. One PATCH that also preserves language.
  const switchAudio = async (value: string) => {
    if (value === audio || !user) return
    setError(null)
    try {
      await api.updatePreferences(user, { preferredAudio: value })
      await refresh()
    } catch {
      setError('Could not save audio preference.')
    }
  }

  if (!detail || !anime) return <p className="muted">{error ?? 'Loading…'}</p>

  const title = anime.titleEnglish ?? anime.titleRomaji
  const totalEpisodes = anime.episodes ?? Infinity
  // Carry the active source forward, else Prev/Next drops ?source= and the player silently
  // reverts to the default one — the same source the user picked on, or the per-source page.
  const goToEpisode = (ep: number) => navigate(`/anime/${id}/watch/${ep}${activeSource ? `?source=${activeSource.id}` : ''}`)

  return (
    <div className="anime-watch">
      <div className="watch-head">
        <Link to={`/anime/${id}`} className="muted">
          ← {title}
        </Link>
        <h1>
          {title} · Episode {episode}
        </h1>
      </div>

      {notAired && (
        <div className="card">
          <p className="muted">{airedThrough === 0 ? 'Coming soon — this anime hasn\'t aired yet.' : `Episode ${episode} hasn't aired yet.`}</p>
        </div>
      )}

      {!notAired && embeddable.length === 0 && (
        <div className="card">
          <p className="muted">No in-page source available for this anime yet.</p>
          <button type="button" className="link-button" onClick={() => navigate(`/anime/${id}`)}>
            Go to the anime page to watch on another site
          </button>
        </div>
      )}

      {!notAired && activeSource && frameUrl && (
        <div className="watch-player card">
          <iframe
            key={`${activeSource.id}-${episode}-${audio}`}
            src={frameUrl}
            className="watch-iframe"
            allowFullScreen
            referrerPolicy="no-referrer"
            sandbox={activeSource.sandboxed ? 'allow-scripts allow-same-origin allow-forms' : undefined}
            title={`${title} episode ${episode} on ${activeSource.name}`}
          />
        </div>
      )}

      <div className="watch-controls">
        <div className="watch-controls-group">
          <span className="stepper">
            <button type="button" disabled={episode <= 1} onClick={() => goToEpisode(episode - 1)}>
              ◀ Prev
            </button>
            <button type="button" disabled={episode >= Math.min(totalEpisodes, airedThrough)} onClick={() => goToEpisode(episode + 1)}>
              Next ▶
            </button>
          </span>
          {activeSource && canSwitchAudio(activeSource.embedUrlTemplate) && (
            <span className="stepper" role="group" aria-label="Audio form">
              {Object.keys(AUDIO_LABELS).map((form) => (
                <button key={form} type="button" className={audio === form ? 'primary' : ''} onClick={() => switchAudio(form)}>
                  {AUDIO_LABELS[form]}
                </button>
              ))}
            </span>
          )}
          {!notAired && (
            <button type="button" className="primary" disabled={entry === undefined || marked || (!!entry && entry.progress >= episode)} onClick={markWatched}>
              {marked || (entry && entry.progress >= episode) ? 'Watched ✓' : '✓ Mark as watched'}
            </button>
          )}
        </div>
        <div className="watch-controls-group">
          {activeSource && (
            <button
              type="button"
              className="link-button"
              onClick={() => window.open(resolveWatchUrl(activeSource, link(activeSource), id, anime.malId, title, episode, audio), '_blank', 'noopener,noreferrer')}
            >
              Open on site ↗
            </button>
          )}
          {!notAired && activeSource && frameUrl && (
            <button type="button" className="link-button report" onClick={reportBroken} disabled={reported}>
              {reported ? 'Reported ✓' : '⚠ Report problem'}
            </button>
          )}
        </div>
      </div>

      {embeddable.length > 1 &&
        groupByLanguage(embeddable, language).map(({ language: lang, items }, i) => (
          <details key={lang} className="source-language-group" open={i === 0}>
            <summary>{i === 0 ? 'Source' : `${LANGUAGE_LABELS[lang] ?? lang} sites (${items.length})`}</summary>
            <div className="source-buttons">
              {items.map((source) => (
                <button
                  key={source.id}
                  type="button"
                  className={source.id === activeSource?.id ? 'primary' : ''}
                  onClick={() => setSourceId(source.id)}
                >
                  {source.name}
                </button>
              ))}
            </div>
          </details>
        ))}

      {error && <p role="alert">{error}</p>}
    </div>
  )
}
