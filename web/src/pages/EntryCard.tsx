import { useNavigate, type NavigateFunction } from 'react-router-dom'
import { api, type ListEntry, type Source } from '../api'
import { useAuth } from '../AuthContext'
import { orderSources } from '../sourceLanguage'
import { STATUS_LABELS } from '../status'
import { canFrame, hasWatchTarget, linkFor, resolveWatchUrl } from '../watchUrl'
import './EntryCard.css'

export type UpdateEntry = (anilistId: number, patch: Parameters<typeof api.updateEntry>[1]) => Promise<void>

export const entryTitle = (entry: ListEntry) => entry.titleEnglish ?? entry.titleRomaji

// First source we can actually deep-link to, in orderSources order (preferred language
// first, last used leading its own language). Undefined if none can.
export function watchableSource(entry: ListEntry, sources: Source[], preferredLanguage: string): Source | undefined {
  const link = (source: Source) => linkFor(entry.sourceLinks, source)
  return orderSources(sources, preferredLanguage, entry.lastSourceId).find((s) => hasWatchTarget(s, link(s), entry.malId))
}

// Next episode on this source: AniTether's own watch page when the source can be framed
// there, else the source's site in a new tab.
export function watchNext(entry: ListEntry, source: Source, update: UpdateEntry, audio: string, navigate: NavigateFunction) {
  const link = linkFor(entry.sourceLinks, source)
  if (canFrame(source, link, entry.malId)) return navigate(`/anime/${entry.anilistId}/watch/${entry.progress + 1}?source=${source.id}`)
  window.open(resolveWatchUrl(source, link, entry.anilistId, entry.malId, entryTitle(entry), entry.progress + 1, audio), '_blank', 'noopener,noreferrer')
  update(entry.anilistId, { lastSourceId: source.id })
}

type Props = {
  entry: ListEntry
  sources: Source[]
  update: UpdateEntry
  onOpen: () => void
}

export function EntryCard({ entry, sources, update, onOpen }: Props) {
  const { language, audio } = useAuth()
  const title = entryTitle(entry)
  const navigate = useNavigate()
  const preferredSource = watchableSource(entry, sources, language)
  // Any in-site player wins over a new tab; the watch page then picks which one (last used first).
  const framed = sources.some((s) => canFrame(s, linkFor(entry.sourceLinks, s), entry.malId))
  const finished = entry.episodes != null && entry.progress >= entry.episodes
  const percent = entry.episodes ? Math.min(100, (entry.progress / entry.episodes) * 100) : 0

  return (
    <article className="entry-card">
      <button type="button" className="entry-cover cover" onClick={onOpen} title={`Edit ${title}`}>
        {entry.coverUrl ? <img src={entry.coverUrl} alt="" loading="lazy" /> : <span className="cover-fallback">{title}</span>}
        <span className={`status-tag status-${entry.status}`}>{STATUS_LABELS[entry.status]}</span>
      </button>
      <div className="entry-body">
        <button type="button" className="entry-title" onClick={onOpen}>
          {title}
        </button>
        <div className="entry-progress">
          <div className="bar">
            <div style={{ width: `${percent}%` }} />
          </div>
          <span className="muted">
            {entry.progress} / {entry.episodes ?? '?'}
          </span>
        </div>
        <div className="entry-actions">
          {(framed || preferredSource) && !finished && (
            <button
              type="button"
              className="primary"
              onClick={() =>
                framed
                  ? navigate(`/anime/${entry.anilistId}/watch/${entry.progress + 1}`)
                  : watchNext(entry, preferredSource!, update, audio, navigate)
              }
              title={`Watch episode ${entry.progress + 1}${framed ? '' : ` on ${preferredSource!.name}`}`}
            >
              ▶ Ep {entry.progress + 1}
            </button>
          )}
          <button
            type="button"
            disabled={finished}
            onClick={() => update(entry.anilistId, { progress: entry.progress + 1 })}
            title="Mark next episode watched"
          >
            +1
          </button>
        </div>
      </div>
    </article>
  )
}
