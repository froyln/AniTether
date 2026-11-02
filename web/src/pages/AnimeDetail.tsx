import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api, type AnimeDetail as AnimeDetailData, type ListEntry, type ListStatus, type Source } from '../api'
import { lastAired } from '../airing'
import { useAuth } from '../AuthContext'
import { AIRING_STATUSES, FORMATS } from '../catalogFilters'
import { groupByLanguage, LANGUAGE_LABELS, orderSources } from '../sourceLanguage'
import { STATUSES, STATUS_LABELS } from '../status'
import { canFrame, hasWatchTarget, linkFor, resolveWatchUrl } from '../watchUrl'
import { SourceLinkForm } from './SourceLinkForm'
import './AnimeDetail.css'

const RANGE = 100 // episodes per range tab on long shows

const open = (url: string) => window.open(url, '_blank', 'noopener,noreferrer')

// AniList descriptions (asHtml: false) still carry <br> and <i>; show as plain text
const plainText = (html: string) =>
  html
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<[^>]+>/g, '')
    .replace(/\n{3,}/g, '\n\n')
    .trim()

export function AnimeDetail() {
  const { anilistId } = useParams<{ anilistId: string }>()
  const id = Number(anilistId)
  const { language, audio } = useAuth()
  const navigate = useNavigate()

  const [detail, setDetail] = useState<AnimeDetailData | null>(null)
  const [allSources, setSources] = useState<Source[]>([])
  const [entry, setEntry] = useState<ListEntry | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [expanded, setExpanded] = useState(false)
  const [selected, setSelected] = useState<number | null>(null)
  const [range, setRange] = useState<number | null>(null)

  const loadDetail = () => api.getAnimeDetail(id).then(setDetail)

  const loadEntry = () => api.getEntry(id).then(setEntry)

  useEffect(() => {
    let stale = false
    setDetail(null)
    setEntry(null)
    setError(null)
    setSelected(null)
    setRange(null)
    api.getAnimeDetail(id)
      .then((value) => { if (!stale) setDetail(value) })
      .then(() => api.resolveLinks(id).then((d) => !stale && setDetail(d)).catch(() => {}))
      .catch(() => !stale && setError('Could not load this anime.'))
    api.getSources().then((value) => { if (!stale) setSources(value) })
      .catch(() => { if (!stale) setError('Could not load sources.') })
    api.getEntry(id).then((value) => { if (!stale) setEntry(value) })
      .catch(() => { if (!stale) setError('Could not load your list entry.') })
    return () => {
      stale = true
    }
  }, [id])

  // Hoisted above the early return: the episode grid needs the same default episode
  // the page renders, and duplicating the arithmetic is how the two would drift apart.
  const totalEpisodes = detail?.anime.episodes ?? (detail?.anime.nextAiringEpisode ? detail.anime.nextAiringEpisode - 1 : 0)
  const progress = entry?.progress ?? 0
  const nextEpisode = totalEpisodes ? Math.min(progress + 1, totalEpisodes) : progress + 1
  const episode = selected ?? nextEpisode

  if (!detail) return <p className="muted">{error ?? 'Loading…'}</p>

  const { anime, sourceLinks } = detail
  const title = anime.titleEnglish ?? anime.titleRomaji
  const finished = !!anime.episodes && progress >= anime.episodes
  // Announced but not aired: no episode exists anywhere yet, so no grid and no ▶ buttons.
  // Still airing: episodes past the last aired one exist only as numbers.
  const comingSoon = anime.airingStatus === 'NOT_YET_RELEASED'
  const airedThrough = lastAired(anime)
  const ordered = orderSources(allSources, language, entry?.lastSourceId)
  const preferredSource = ordered.find((s) => hasWatchTarget(s, linkFor(sourceLinks, s), anime.malId))
  const embeddableSource = ordered.find((s) => canFrame(s, linkFor(sourceLinks, s), anime.malId))
  const description = anime.description ? plainText(anime.description) : null

  const ranges = Math.ceil(totalEpisodes / RANGE)
  const activeRange = range ?? Math.floor((episode - 1) / RANGE)
  const first = activeRange * RANGE + 1
  const episodes = Array.from({ length: Math.max(0, Math.min(RANGE, totalEpisodes - first + 1)) }, (_, i) => first + i)

  const update = async (patch: Parameters<typeof api.updateEntry>[1]) => {
    try {
      await api.updateEntry(id, patch)
      await loadEntry()
    } catch {
      setError('Could not update your list.')
    }
  }

  const addToList = async () => {
    try {
      await api.addToList(id)
      await loadEntry()
    } catch {
      setError('Could not add to list.')
    }
  }

  const removeFromList = async () => {
    if (!window.confirm(`Remove ${title} from your list?`)) return
    try {
      await api.removeFromList(id)
      setEntry(null)
    } catch {
      setError('Could not remove from list.')
    }
  }

  const watch = (source: Source, ep: number) => {
    const link = linkFor(sourceLinks, source)
    open(resolveWatchUrl(source, link, id, anime.malId, title, ep, audio))
    if (entry) api.updateEntry(id, { lastSourceId: source.id }).catch(() => setError('Could not save the last source.'))
  }

  const renderSourceButtons = (list: Source[]) =>
    list.map((source) =>
      hasWatchTarget(source, linkFor(sourceLinks, source), anime.malId) ? (
        <button key={source.id} type="button" className="primary" onClick={() => watch(source, episode)}>
          ▶ {source.name}
        </button>
      ) : null,
    )

  const chips = [
    anime.format && (FORMATS[anime.format] ?? anime.format),
    anime.episodes ? `${anime.episodes} episodes` : null,
    anime.airingStatus && (AIRING_STATUSES[anime.airingStatus] ?? anime.airingStatus),
    anime.airingStatus === 'RELEASING' && anime.nextAiringEpisode ? `Ep ${anime.nextAiringEpisode} airing next` : null,
  ].filter(Boolean)

  return (
    <div className="anime-detail">
      <div className="hero" style={anime.bannerUrl?.startsWith('https://') ? { backgroundImage: `url("${encodeURI(anime.bannerUrl)}")` } : undefined} />

      <div className="detail-head">
        {anime.coverUrl ? <img className="detail-cover" src={anime.coverUrl} alt="" /> : <div className="detail-cover cover-fallback" />}
        <div className="detail-title">
          <h1>{title}</h1>
          {anime.titleEnglish && anime.titleEnglish !== anime.titleRomaji && <p className="muted romaji">{anime.titleRomaji}</p>}
          <div className="meta-chips">
            {chips.map((c) => (
              <span key={c as string} className="meta-chip">
                {c}
              </span>
            ))}
          </div>
          {anime.genres.length > 0 && (
            <div className="genre-links">
              {anime.genres.map((g) => (
                <Link key={g} to={`/catalog?${new URLSearchParams({ genres: g, sort: 'popular' })}`}>
                  {g}
                </Link>
              ))}
            </div>
          )}
        </div>
      </div>

      <div className="detail-body">
        <aside className="card action-panel">
          {/* Plays the episode picked in the grid; with none picked, the next unwatched one.
              A finished anime shows it only once an episode is picked (a rewatch). */}
          {preferredSource && (!finished || selected !== null) && episode <= airedThrough && (
            <button
              type="button"
              className="primary big"
              onClick={() => (embeddableSource ? navigate(`/anime/${id}/watch/${episode}`) : watch(preferredSource, episode))}
            >
              ▶ {episode !== nextEpisode || finished ? 'Play' : progress === 0 ? 'Start' : 'Continue'} ep {episode}
              <small>on {embeddableSource?.name ?? preferredSource.name}</small>
            </button>
          )}
          {entry ? (
            <>
              <label>
                Status
                <select value={entry.status} onChange={(e) => update({ status: e.target.value as ListStatus })}>
                  {STATUSES.map((status) => (
                    <option key={status} value={status}>
                      {STATUS_LABELS[status]}
                    </option>
                  ))}
                </select>
              </label>
              <div className="progress-row">
                <span>
                  Progress <strong>{progress}</strong> / {anime.episodes ?? '?'}
                </span>
                <span className="stepper">
                  <button type="button" disabled={progress === 0} onClick={() => update({ progress: progress - 1 })} aria-label="One episode less">
                    −1
                  </button>
                  <button type="button" disabled={finished} onClick={() => update({ progress: progress + 1 })}>
                    +1
                  </button>
                </span>
              </div>
              <label>
                Score
                <select value={entry.score ?? ''} onChange={(e) => update({ score: Number(e.target.value) })}>
                  {entry.score == null && <option value="">—</option>}
                  {Array.from({ length: 10 }, (_, i) => 10 - i).map((n) => (
                    <option key={n} value={n}>
                      {n}
                    </option>
                  ))}
                </select>
              </label>
              <button type="button" className="link-button danger remove" onClick={removeFromList}>
                Remove from list
              </button>
            </>
          ) : (
            <button type="button" className="primary big" onClick={addToList}>
              + Add to list
            </button>
          )}
          {error && <p role="alert">{error}</p>}
        </aside>

        <div className="detail-main">
          {description && (
            <section>
              <p className={`description ${expanded ? '' : 'clamped'}`}>{description}</p>
              {description.length > 400 && (
                <button type="button" className="link-button" onClick={() => setExpanded((x) => !x)}>
                  {expanded ? 'Show less' : 'Show more'}
                </button>
              )}
            </section>
          )}

          {comingSoon && (
            <section className="card coming-soon">
              <h2>Coming soon</h2>
              <p className="muted">This anime hasn't aired yet, so no site has its episodes. Add it to your list to keep it in sight.</p>
            </section>
          )}

          {!comingSoon && totalEpisodes > 0 && (
            <section>
              <h2>Episodes</h2>
              {ranges > 1 && (
                <div className="range-tabs">
                  {Array.from({ length: ranges }, (_, r) => (
                    <button key={r} type="button" className={r === activeRange ? 'active' : ''} onClick={() => setRange(r)}>
                      {r * RANGE + 1}–{Math.min((r + 1) * RANGE, totalEpisodes)}
                    </button>
                  ))}
                </div>
              )}
              <div className="episode-grid">
                {episodes.map((ep) => (
                  <button
                    key={ep}
                    type="button"
                    className={[ep <= progress && 'watched', ep === nextEpisode && !finished && 'next', ep === episode && 'selected', ep > airedThrough && 'unaired']
                      .filter(Boolean)
                      .join(' ')}
                    onClick={() => setSelected(ep)}
                    aria-label={`Episode ${ep}${ep <= progress ? ', watched' : ep > airedThrough ? ', not aired yet' : ''}`}
                  >
                    {ep}
                  </button>
                ))}
              </div>

              <div className="card episode-panel">
                <div className="episode-panel-head">
                  <h3>
                    Episode {episode} {episode <= progress && <span className="muted">· watched</span>}
                  </h3>
                  {entry && episode > progress && episode <= airedThrough && (
                    <button type="button" onClick={() => update({ progress: episode })}>
                      ✓ Mark watched{episode > progress + 1 ? ` up to ep ${episode}` : ''}
                    </button>
                  )}
                  {entry && episode <= progress && (
                    <button type="button" onClick={() => update({ progress: episode - 1 })}>
                      Mark unwatched{episode < progress ? ` from ep ${episode}` : ''}
                    </button>
                  )}
                </div>

                {episode > airedThrough ? (
                  <div className="panel-section">
                    <p className="muted">Not aired yet{episode === airedThrough + 1 ? ' — this is the next episode to air.' : '.'}</p>
                  </div>
                ) : (
                  <>
                    <div className="panel-section">
                      <h4>Watch on</h4>
                      {allSources.length === 0 && <p className="muted">No streaming sources configured.</p>}
                      {allSources.length > 0 && allSources.every((s) => !hasWatchTarget(s, linkFor(sourceLinks, s), anime.malId)) && (
                        <p className="muted">Not linked to a source yet — link one below.</p>
                      )}
                      {groupByLanguage(allSources, language).map(({ language: lang, items }, i) =>
                        i === 0 ? (
                          <div key={lang} className="source-buttons">
                            {renderSourceButtons(items)}
                          </div>
                        ) : (
                          <details key={lang} className="source-language-group">
                            <summary>
                              {LANGUAGE_LABELS[lang] ?? lang} sites ({items.length})
                            </summary>
                            <div className="source-buttons">{renderSourceButtons(items)}</div>
                          </details>
                        ),
                      )}
                    </div>
                  </>
                )}

                <div className="panel-section">
                  <SourceLinkForm anilistId={id} title={title} malId={anime.malId} sources={allSources} links={sourceLinks} onSaved={loadDetail} />
                </div>
              </div>
            </section>
          )}
        </div>
      </div>
    </div>
  )
}
