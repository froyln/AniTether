import { useState, type FormEvent } from 'react'
import { api, type Source, type SourceLink } from '../api'
import { hasWatchTarget, linkFor } from '../watchUrl'
import './SourceLinkForm.css'

type Props = {
  anilistId: number
  title: string
  malId: number | null
  sources: Source[]
  links: SourceLink[]
  onSaved: () => void
}

export function SourceLinkForm({ anilistId, title, malId, sources, links, onSaved }: Props) {
  const missing = sources.filter((s) => !hasWatchTarget(s, linkFor(links, s), malId))
  const [sourceId, setSourceId] = useState(missing[0]?.id ?? sources[0]?.id ?? '')
  const [url, setUrl] = useState('')
  const [error, setError] = useState<string | null>(null)

  if (sources.length === 0) return null

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    try {
      await api.setSourceLink(sourceId, anilistId, url)
      setUrl('')
      onSaved()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not set link.')
    }
  }

  return (
    <details className="source-link">
      <summary>Link this anime to a source site</summary>
      {missing.length > 0 && (
        <>
          <p className="muted">Not linked yet. Paste the anime's page (or any of its episodes) on that site, or find it:</p>
          <p className="muted">
            {missing.map((s) => (
              <button
                key={s.id}
                type="button"
                className="link-button"
                onClick={() => window.open(s.searchUrlTemplate.replace('{title}', encodeURIComponent(title)), '_blank', 'noopener,noreferrer')}
              >
                🔎 {s.name}
              </button>
            ))}
          </p>
        </>
      )}
      <form onSubmit={onSubmit} className="link-form">
        <select value={sourceId} onChange={(e) => setSourceId(e.target.value)} aria-label="Source">
          {sources.map((source) => (
            <option key={source.id} value={source.id}>
              {source.name}
              {links.some((l) => l.sourceId === source.id) ? ' ✓' : ''}
            </option>
          ))}
        </select>
        <input value={url} onChange={(e) => setUrl(e.target.value)} placeholder="https://…" required />
        <button type="submit">Set link</button>
      </form>
      {error && <p role="alert">{error}</p>}
    </details>
  )
}
