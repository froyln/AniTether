import { useEffect, useState, type ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, type Anime, type ListEntry, type Source } from '../api'
import { SEASONS, seasonAt } from '../catalogFilters'
import { AnimeCard } from './AnimeCard'
import { EntryCard } from './EntryCard'
import './Home.css'

const now = seasonAt(new Date())

const ROWS: { title: string; filters: Record<string, string> }[] = [
  { title: 'Trending now', filters: { sort: 'trending' } },
  { title: `Popular this season · ${SEASONS[now.season]} ${now.year}`, filters: { season: now.season, year: String(now.year), sort: 'popular' } },
  { title: 'All-time top rated', filters: { sort: 'score' } },
]

export function Home() {
  const navigate = useNavigate()
  const [list, setList] = useState<ListEntry[] | null>(null)
  const [sources, setSources] = useState<Source[]>([])
  const [sourceError, setSourceError] = useState(false)

  const loadList = () => api.getList().then(setList)

  useEffect(() => {
    loadList()
    api.getWatchSources().then(setSources).catch(() => setSourceError(true))
  }, [])

  const update = async (anilistId: number, patch: Parameters<typeof api.updateEntry>[1]) => {
    await api.updateEntry(anilistId, patch)
    loadList()
  }

  const add = async (anilistId: number) => {
    await api.addToList(anilistId)
    loadList()
  }

  const watching = (list ?? [])
    .filter((e) => e.status === 'Watching' || e.status === 'Rewatching')
    .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))
  const inList = new Set((list ?? []).map((e) => e.anilistId))

  return (
    <div className="home">
      {sourceError && <p role="alert">Could not load watch sources.</p>}
      {watching.length > 0 && (
        <Row title="Continue watching" seeAll="/list?status=Watching">
          {watching.map((entry) => (
            <EntryCard
              key={entry.anilistId}
              entry={entry}
              sources={sources}
              update={update}
              onOpen={() => navigate(`/anime/${entry.anilistId}`)}
            />
          ))}
        </Row>
      )}
      {ROWS.map((row) => (
        <CatalogRow key={row.title} title={row.title} filters={row.filters} inList={inList} onAdd={add} />
      ))}
    </div>
  )
}

function CatalogRow({
  title,
  filters,
  inList,
  onAdd,
}: {
  title: string
  filters: Record<string, string>
  inList: Set<number>
  onAdd: (anilistId: number) => void
}) {
  const [anime, setAnime] = useState<Anime[] | null>(null)
  const [error, setError] = useState(false)

  useEffect(() => {
    api.browseCatalog(filters, 1).then(setAnime).catch(() => setError(true))
  }, [])

  return (
    <Row title={title} seeAll={`/catalog?${new URLSearchParams(filters)}`}>
      {error && <p className="muted">Couldn't load this row.</p>}
      {!anime && !error && Array.from({ length: 8 }, (_, i) => <div key={i} className="cover skeleton" />)}
      {anime?.map((a) => <AnimeCard key={a.anilistId} anime={a} inList={inList.has(a.anilistId)} onAdd={onAdd} />)}
    </Row>
  )
}

function Row({ title, seeAll, children }: { title: string; seeAll: string; children: ReactNode }) {
  return (
    <section className="home-row">
      <header>
        <h2>{title}</h2>
        <Link to={seeAll}>See all →</Link>
      </header>
      <div className="row-scroller">{children}</div>
    </section>
  )
}
