import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useAuth } from '../AuthContext'
import { api, type Source } from '../api'
import { LANGUAGE_LABELS } from '../sourceLanguage'
import './Settings.css'

const EMPTY = { name: '', baseUrl: '', searchUrlTemplate: '', episodeUrlTemplate: '', animePageUrlTemplate: '', embedUrlTemplate: '', language: 'en', sandboxed: true, allowPageEmbed: false }

type TextFieldKey = Exclude<keyof typeof EMPTY, 'language' | 'sandboxed' | 'allowPageEmbed'>

/** The form's shape for an existing source: empty string where a template is unset. */
const formFrom = (s: Source) => ({
  name: s.name,
  baseUrl: s.baseUrl,
  searchUrlTemplate: s.searchUrlTemplate,
  episodeUrlTemplate: s.episodeUrlTemplate ?? '',
  animePageUrlTemplate: s.animePageUrlTemplate ?? '',
  embedUrlTemplate: s.embedUrlTemplate ?? '',
  language: s.language ?? 'en',
  sandboxed: s.sandboxed,
  allowPageEmbed: s.allowPageEmbed,
})

const FIELDS: { key: TextFieldKey; label: string; placeholder: string; hint?: string; required?: boolean }[] = [
  { key: 'name', label: 'Name', placeholder: 'Example', required: true },
  { key: 'baseUrl', label: 'Base URL', placeholder: 'https://example.com', required: true },
  { key: 'searchUrlTemplate', label: 'Search URL', placeholder: 'https://example.com/search?q={title}', hint: 'Must contain {title}.', required: true },
  {
    key: 'episodeUrlTemplate',
    label: 'Episode URL',
    placeholder: 'https://example.com/watch/{slug}?ep={episode}',
    hint: 'Optional. {episode} required; {anilist_id} or {slug} (needs a saved per-anime link).',
  },
  {
    key: 'animePageUrlTemplate',
    label: 'Anime page URL',
    placeholder: 'https://example.com/anime/{slug}',
    hint: "Optional. Only for auto-linking when the site's episode player lives under a different path than its anime page — {slug} required.",
  },
  {
    key: 'embedUrlTemplate',
    label: 'Embed URL',
    placeholder: 'https://example.com/embed/ani/{anilist_id}/{episode}',
    hint: 'Optional — an iframe-embeddable player URL, shown on the in-site watch page. {episode} required.',
  },
]

export function AdminSources() {
  const { user } = useAuth()
  const [sources, setSources] = useState<Source[]>([])
  const [error, setError] = useState<string | null>(null)
  const [form, setForm] = useState(EMPTY)
  // Set while the form edits an existing source instead of adding one.
  const [editingId, setEditingId] = useState<string | null>(null)
  const formSection = useRef<HTMLElement>(null)

  const load = () => api.getAllSources().then(setSources).catch(() => setError('Could not load sources.'))

  useEffect(() => {
    if (user?.isAdmin) load()
  }, [user])

  if (!user?.isAdmin) {
    return <p role="alert">Not authorized.</p>
  }

  const startEdit = (source: Source) => {
    setError(null)
    setEditingId(source.id)
    setForm(formFrom(source))
    formSection.current?.scrollIntoView({ behavior: 'smooth' })
  }

  const stopEdit = () => {
    setError(null)
    setEditingId(null)
    setForm(EMPTY)
  }

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    try {
      if (editingId) {
        // Every field is sent: an empty template string clears it on the server.
        await api.updateSource(editingId, form)
        stopEdit()
        load()
        return
      }
      await api.createSource({
        ...form,
        episodeUrlTemplate: form.episodeUrlTemplate || null,
        animePageUrlTemplate: form.animePageUrlTemplate || null,
        embedUrlTemplate: form.embedUrlTemplate || null,
        enabled: true,
      })
      setForm(EMPTY)
      load()
    } catch (err) {
      setError(err instanceof Error ? err.message : editingId ? 'Could not save source.' : 'Could not create source.')
    }
  }

  const toggleEnabled = async (source: Source) => {
    setError(null)
    try {
      await api.updateSource(source.id, { enabled: !source.enabled })
      load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not update source.')
    }
  }

  return (
    <div>
      <h1>Streaming sources</h1>
      {error && <p role="alert">{error}</p>}

      <section className="card settings-section">
        <table className="settings-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Language</th>
              <th>Episode URL</th>
              <th>Status</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {sources.map((source) => (
              <tr key={source.id}>
                <td>
                  <strong>{source.name}</strong>
                  <div className="muted">{source.baseUrl}</div>
                </td>
                <td className="muted">{source.language ? (LANGUAGE_LABELS[source.language] ?? source.language) : '—'}</td>
                <td className="muted">
                  <code>{source.episodeUrlTemplate ?? '—'}</code>
                </td>
                <td>{source.enabled ? 'Enabled' : 'Disabled'}</td>
                <td>
                  <button type="button" onClick={() => startEdit(source)}>
                    Edit
                  </button>{' '}
                  <button type="button" className={source.enabled ? 'danger' : 'primary'} onClick={() => toggleEnabled(source)}>
                    {source.enabled ? 'Disable' : 'Enable'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section ref={formSection} className="card settings-section admin-form">
        <h2>{editingId ? `Edit ${sources.find((s) => s.id === editingId)?.name ?? 'source'}` : 'Add source'}</h2>
        <form onSubmit={onSubmit}>
          {FIELDS.map((f) => (
            <label key={f.key}>
              {f.label}
              <input
                value={form[f.key]}
                onChange={(e) => setForm((prev) => ({ ...prev, [f.key]: e.target.value }))}
                placeholder={f.placeholder}
                required={f.required}
              />
              {f.hint && <small className="muted">{f.hint}</small>}
            </label>
          ))}
          <label>
            Language
            <select value={form.language} onChange={(e) => setForm((prev) => ({ ...prev, language: e.target.value }))}>
              {Object.entries(LANGUAGE_LABELS).map(([code, label]) => (
                <option key={code} value={code}>
                  {label}
                </option>
              ))}
            </select>
          </label>
          <label className="checkbox-label">
            <input
              type="checkbox"
              checked={form.sandboxed}
              onChange={(e) => setForm((prev) => ({ ...prev, sandboxed: e.target.checked }))}
            />
            Sandbox the embed iframe (blocks popups; some players refuse to run sandboxed)
          </label>
          <label className="checkbox-label">
            <input
              type="checkbox"
              checked={form.allowPageEmbed}
              onChange={(e) => setForm((prev) => ({ ...prev, allowPageEmbed: e.target.checked }))}
            />
            Allow embedding its own episode page (framable + playback verified per source)
          </label>
          <div>
            <button type="submit" className="primary">
              {editingId ? 'Save changes' : 'Add source'}
            </button>{' '}
            {editingId && (
              <button type="button" onClick={stopEdit}>
                Cancel
              </button>
            )}
          </div>
        </form>
      </section>
    </div>
  )
}
