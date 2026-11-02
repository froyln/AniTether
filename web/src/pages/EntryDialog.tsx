import { useEffect, useRef } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { type ListEntry, type ListStatus, type Source } from '../api'
import { useAuth } from '../AuthContext'
import { groupByLanguage, LANGUAGE_LABELS } from '../sourceLanguage'
import { STATUSES, STATUS_LABELS } from '../status'
import { entryTitle, watchNext, type UpdateEntry } from './EntryCard'
import { SourceLinkForm } from './SourceLinkForm'

type Props = {
  entry: ListEntry
  sources: Source[]
  update: UpdateEntry
  remove: (anilistId: number) => Promise<void>
  refresh: () => void
  onClose: () => void
}

export function EntryDialog({ entry, sources, update, remove, refresh, onClose }: Props) {
  const { language, audio } = useAuth()
  const navigate = useNavigate()
  const dialog = useRef<HTMLDialogElement>(null)
  const title = entryTitle(entry)

  useEffect(() => {
    dialog.current?.showModal()
  }, [])

  const onRemove = async () => {
    if (!window.confirm(`Remove ${title} from your list?`)) return
    await remove(entry.anilistId)
    onClose()
  }

  const setProgress = (value: number) => {
    if (Number.isInteger(value) && value >= 0 && value !== entry.progress) update(entry.anilistId, { progress: value })
  }

  return (
    <dialog
      ref={dialog}
      className="entry-dialog"
      onClose={onClose}
      onClick={(e) => e.target === dialog.current && dialog.current.close()}
    >
      <header>
        {entry.coverUrl && <img src={entry.coverUrl} alt="" />}
        <div>
          <h2>{title}</h2>
          <Link to={`/anime/${entry.anilistId}`}>Open anime page</Link>
        </div>
        <button type="button" className="close" onClick={() => dialog.current?.close()} aria-label="Close">
          ✕
        </button>
      </header>

      <div className="dialog-grid">
        <label>
          Status
          <select value={entry.status} onChange={(e) => update(entry.anilistId, { status: e.target.value as ListStatus })}>
            {STATUSES.map((status) => (
              <option key={status} value={status}>
                {STATUS_LABELS[status]}
              </option>
            ))}
          </select>
        </label>
        <label>
          Score
          <select
            value={entry.score ?? ''}
            onChange={(e) => update(entry.anilistId, { score: Number(e.target.value) })}
          >
            {entry.score == null && <option value="">—</option>}
            {Array.from({ length: 10 }, (_, i) => 10 - i).map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </label>
        <label>
          Progress
          <span className="progress-edit">
            <input
              key={entry.progress}
              type="number"
              min={0}
              max={entry.episodes ?? undefined}
              defaultValue={entry.progress}
              onBlur={(e) => setProgress(e.target.valueAsNumber)}
              onKeyDown={(e) => e.key === 'Enter' && setProgress(e.currentTarget.valueAsNumber)}
            />
            <span className="muted">/ {entry.episodes ?? '?'}</span>
            <button type="button" disabled={entry.progress === 0} onClick={() => setProgress(entry.progress - 1)} aria-label="One episode less">
              −1
            </button>
            <button type="button" onClick={() => setProgress(entry.progress + 1)}>
              +1
            </button>
          </span>
        </label>
      </div>

      <label className="notes">
        Notes
        <textarea maxLength={2000}
          rows={3}
          defaultValue={entry.notes ?? ''}
          onBlur={(e) => e.target.value !== (entry.notes ?? '') && update(entry.anilistId, { notes: e.target.value })}
        />
      </label>

      {sources.length > 0 && (
        <section>
          <h3>Watch episode {entry.progress + 1}</h3>
          {groupByLanguage(sources, language).map(({ language: lang, items }, i) =>
            i === 0 ? (
              <div key={lang} className="source-buttons">
                {items.map((source) => (
                  <button key={source.id} type="button" onClick={() => watchNext(entry, source, update, audio, navigate)}>
                    {source.name}
                  </button>
                ))}
              </div>
            ) : (
              <details key={lang} className="source-language-group">
                <summary>
                  {LANGUAGE_LABELS[lang] ?? lang} sites ({items.length})
                </summary>
                <div className="source-buttons">
                  {items.map((source) => (
                    <button key={source.id} type="button" onClick={() => watchNext(entry, source, update, audio, navigate)}>
                      {source.name}
                    </button>
                  ))}
                </div>
              </details>
            ),
          )}
          <SourceLinkForm anilistId={entry.anilistId} title={title} malId={entry.malId} sources={sources} links={entry.sourceLinks} onSaved={refresh} />
        </section>
      )}

      <footer>
        <button type="button" className="danger" onClick={onRemove}>
          Remove from list
        </button>
      </footer>
    </dialog>
  )
}
