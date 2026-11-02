import { useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api, type Anime } from '../api'
import { AIRING_STATUSES, FILTER_KEYS, FORMATS, GENRES, SEASONS, SORTS } from '../catalogFilters'
import { AnimeCard } from './AnimeCard'
import './Catalog.css'

const PAGE_SIZE = 20 // AniListClient.BrowseAsync perPage

type CatalogState = { key: string; items: Anime[]; page: number; done: boolean }

// Survives unmount so going back from an anime page restores loaded items and scroll offset.
// ponytail: one cached query only, keyed map if people bounce between searches.
let cache: CatalogState | null = null
let cachedScrollY = 0

const fresh = (key: string): CatalogState => ({ key, items: [], page: 0, done: false })

const THIS_YEAR = new Date().getFullYear()
const YEARS = Array.from({ length: THIS_YEAR + 2 - 1970 }, (_, i) => THIS_YEAR + 1 - i)

export function Catalog() {
  const [params, setParams] = useSearchParams()
  const filters: Record<string, string> = {}
  for (const k of FILTER_KEYS) {
    const v = params.get(k)?.trim()
    if (v) filters[k] = v
  }
  const key = new URLSearchParams(filters).toString()
  const selectedGenres = filters.genres ? filters.genres.split(',') : []

  const [state, setState] = useState<CatalogState>(() => (cache?.key === key ? cache : fresh(key)))
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [inList, setInList] = useState<Set<number>>(new Set())
  const sentinel = useRef<HTMLButtonElement>(null)
  const loadingRef = useRef(false)
  const restoring = useRef(cache?.key === key).current // first render only

  cache = state

  useEffect(() => {
    api.getList().then((list) => setInList(new Set(list.map((e) => e.anilistId))))
  }, [])

  // New filters: reset (unless it's the cached query we just restored).
  useEffect(() => {
    if (state.key !== key) {
      setState(fresh(key))
      setError(null)
      window.scrollTo(0, 0)
    }
  }, [key])

  // Restore scroll once on mount, then keep the saved offset current.
  useEffect(() => {
    if (restoring) window.scrollTo(0, cachedScrollY)
    const onScroll = () => {
      cachedScrollY = window.scrollY
    }
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

  const loadMore = async () => {
    if (loadingRef.current || state.done || state.key !== key) return
    loadingRef.current = true
    setLoading(true)
    setError(null)
    try {
      const next = state.page + 1
      const batch = await api.browseCatalog(filters, next)
      setState((s) => {
        if (s.key !== key) return s
        const seen = new Set(s.items.map((a) => a.anilistId))
        const items = [...s.items, ...batch.filter((a) => !seen.has(a.anilistId))] // trending order can shift between pages
        return { ...s, items, page: next, done: batch.length < PAGE_SIZE }
      })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load the catalog.')
    } finally {
      loadingRef.current = false
      setLoading(false)
    }
  }

  // First page loads directly, not via the observer (which doesn't fire in background tabs).
  // Re-checked when a load finishes: a filter change mid-load skips its first load.
  useEffect(() => {
    if (!loading && !error && state.items.length === 0 && !state.done) loadMore()
  }, [state.key, loading])

  useEffect(() => {
    const el = sentinel.current
    if (!el || error) return // after a failure, retry only via the button, not in a loop
    const observer = new IntersectionObserver((entries) => entries[0].isIntersecting && loadMore(), { rootMargin: '600px' })
    observer.observe(el)
    return () => observer.disconnect()
  })

  const add = async (anilistId: number) => {
    try {
      await api.addToList(anilistId)
      setInList((prev) => new Set(prev).add(anilistId))
    } catch {
      setError('Could not add to list.')
    }
  }

  const setFilter = (name: string, value: string) => {
    const next = new URLSearchParams(params)
    if (value) next.set(name, value)
    else next.delete(name)
    setParams(next)
  }

  const toggleGenre = (genre: string) => {
    const next = selectedGenres.includes(genre) ? selectedGenres.filter((g) => g !== genre) : [...selectedGenres, genre]
    setFilter('genres', next.join(','))
  }

  const hasFilters = FILTER_KEYS.some((k) => k !== 'q' && filters[k])
  const heading = filters.q
    ? `Results for “${filters.q}”`
    : [
        selectedGenres.join(', '),
        filters.format && FORMATS[filters.format],
        filters.season && SEASONS[filters.season],
        filters.year,
        filters.status && AIRING_STATUSES[filters.status],
      ]
        .filter(Boolean)
        .join(' · ') || 'Catalog'

  return (
    <div>
      <h1>{heading}</h1>

      <div className="filter-bar">
        <details className="dropdown">
          <summary>{selectedGenres.length ? `Genres (${selectedGenres.length})` : 'Genres'}</summary>
          <div className="dropdown-panel genre-options">
            {GENRES.map((genre) => (
              <label key={genre}>
                <input
                  type="checkbox"
                  checked={selectedGenres.includes(genre)}
                  disabled={!selectedGenres.includes(genre) && selectedGenres.length >= 5}
                  onChange={() => toggleGenre(genre)}
                />
                {genre}
              </label>
            ))}
          </div>
        </details>
        <select value={filters.year ?? ''} onChange={(e) => setFilter('year', e.target.value)} aria-label="Year">
          <option value="">Any year</option>
          {YEARS.map((y) => (
            <option key={y} value={y}>
              {y}
            </option>
          ))}
        </select>
        <FilterSelect label="Season" any="Any season" options={SEASONS} value={filters.season} onChange={(v) => setFilter('season', v)} />
        <FilterSelect label="Format" any="Any format" options={FORMATS} value={filters.format} onChange={(v) => setFilter('format', v)} />
        <FilterSelect label="Status" any="Any status" options={AIRING_STATUSES} value={filters.status} onChange={(v) => setFilter('status', v)} />
        <select
          value={filters.sort ?? (filters.q ? 'relevance' : 'trending')}
          onChange={(e) => setFilter('sort', e.target.value)}
          aria-label="Sort"
          className="sort"
        >
          {Object.entries(SORTS)
            .filter(([value]) => value !== 'relevance' || filters.q)
            .map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
        </select>
        {hasFilters && (
          <button type="button" className="link-button" onClick={() => setParams(filters.q ? { q: filters.q } : {})}>
            Clear filters
          </button>
        )}
      </div>

      {state.done && state.items.length === 0 && <p className="empty card">No anime match these filters.</p>}

      <div className="cover-grid">
        {state.items.map((a) => (
          <AnimeCard key={a.anilistId} anime={a} inList={inList.has(a.anilistId)} onAdd={add} />
        ))}
      </div>

      {error && <p role="alert">{error}</p>}
      {!state.done && (
        <button type="button" ref={sentinel} className="load-more" onClick={loadMore} disabled={loading}>
          {loading ? 'Loading…' : 'Load more'}
        </button>
      )}
    </div>
  )
}

type FilterSelectProps = {
  label: string
  any: string
  options: Record<string, string>
  value: string | undefined
  onChange: (value: string) => void
}

function FilterSelect({ label, any, options, value, onChange }: FilterSelectProps) {
  return (
    <select value={value ?? ''} onChange={(e) => onChange(e.target.value)} aria-label={label}>
      <option value="">{any}</option>
      {Object.entries(options).map(([v, text]) => (
        <option key={v} value={v}>
          {text}
        </option>
      ))}
    </select>
  )
}
