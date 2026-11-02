import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api, type ListEntry, type ListStatus, type Source } from '../api'
import { STATUSES, STATUS_LABELS } from '../status'
import { EntryCard, entryTitle } from './EntryCard'
import { EntryDialog } from './EntryDialog'
import { ProfileShareDialog } from './ProfileShareDialog'
import './MyList.css'

type Sort = 'updated' | 'title' | 'score'

const SORTS: Record<Sort, (a: ListEntry, b: ListEntry) => number> = {
  updated: (a, b) => b.updatedAt.localeCompare(a.updatedAt),
  title: (a, b) => entryTitle(a).localeCompare(entryTitle(b)),
  score: (a, b) => (b.score ?? -1) - (a.score ?? -1),
}

export function MyList() {
  const [entries, setEntries] = useState<ListEntry[] | null>(null)
  const [sources, setSources] = useState<Source[]>([])
  const [sourceError, setSourceError] = useState(false)
  const [params, setParams] = useSearchParams()
  const [filter, setFilter] = useState('')
  const [sort, setSort] = useState<Sort>('updated')
  const [editingId, setEditingId] = useState<number | null>(null)
  const [shareOpen, setShareOpen] = useState(false)

  const statusParam = params.get('status') as ListStatus | null
  const tab: ListStatus | 'All' = statusParam && STATUSES.includes(statusParam) ? statusParam : 'All'

  const load = () => api.getList().then(setEntries)

  useEffect(() => {
    load()
    api.getWatchSources().then(setSources).catch(() => setSourceError(true))
  }, [])

  const update = async (anilistId: number, patch: Parameters<typeof api.updateEntry>[1]) => {
    await api.updateEntry(anilistId, patch)
    load()
  }

  const remove = async (anilistId: number) => {
    await api.removeFromList(anilistId)
    load()
  }

  const all = entries ?? []
  const needle = filter.trim().toLowerCase()
  const visible = all
    .filter((e) => tab === 'All' || e.status === tab)
    .filter((e) => !needle || e.titleRomaji.toLowerCase().includes(needle) || e.titleEnglish?.toLowerCase().includes(needle))
    .sort(SORTS[sort])
  const editing = all.find((e) => e.anilistId === editingId)

  return (
    <div>
      {sourceError && <p role="alert">Could not load watch sources.</p>}
      <div className="mylist-header"><h1>My list</h1><button type="button" onClick={() => setShareOpen(true)}>Share profile</button></div>
      <div className="list-toolbar">
        <div className="tabs" role="tablist">
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'All'}
            className={tab === 'All' ? 'active' : ''}
            onClick={() => setParams({}, { replace: true })}
          >
            All <span className="count">{all.length}</span>
          </button>
          {STATUSES.map((status) => (
            <button
              key={status}
              type="button"
              role="tab"
              aria-selected={status === tab}
              className={status === tab ? 'active' : ''}
              onClick={() => setParams({ status }, { replace: true })}
            >
              {STATUS_LABELS[status]} <span className="count">{all.filter((e) => e.status === status).length}</span>
            </button>
          ))}
        </div>
        <div className="list-controls">
          <input type="search" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="Filter by title" />
          <select value={sort} onChange={(e) => setSort(e.target.value as Sort)} aria-label="Sort">
            <option value="updated">Last updated</option>
            <option value="title">Title</option>
            <option value="score">Score</option>
          </select>
        </div>
      </div>

      {entries && visible.length === 0 && (
        <div className="empty card">
          {needle ? (
            <p>No titles match “{filter}”.</p>
          ) : (
            <>
              <p>{tab === 'All' ? 'Your list is empty.' : `Nothing in ${STATUS_LABELS[tab]} yet.`}</p>
              <Link to="/catalog">Browse the catalog</Link>
            </>
          )}
        </div>
      )}

      <div className="cover-grid">
        {visible.map((entry) => (
          <EntryCard
            key={entry.anilistId}
            entry={entry}
            sources={sources}
            update={update}
            onOpen={() => setEditingId(entry.anilistId)}
          />
        ))}
      </div>

      {editing && (
        <EntryDialog
          key={editing.anilistId}
          entry={editing}
          sources={sources}
          update={update}
          remove={remove}
          refresh={load}
          onClose={() => setEditingId(null)}
        />
      )}
      <ProfileShareDialog open={shareOpen} onClose={() => setShareOpen(false)} />
    </div>
  )
}
