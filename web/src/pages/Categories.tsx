import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { AIRING_STATUSES, FORMATS, GENRES, SEASONS, seasonAt } from '../catalogFilters'
import './Categories.css'

const catalogLink = (filters: Record<string, string>) => `/catalog?${new URLSearchParams(filters)}`

// previous, current, next season
const SEASON_LINKS = [-1, 0, 1].map((offset) => {
  const { season, year } = seasonAt(new Date(), offset)
  const label = offset === 0 ? 'This season' : offset < 0 ? 'Last season' : 'Next season'
  return { label, sub: `${SEASONS[season]} ${year}`, to: catalogLink({ season, year: String(year), sort: 'popular' }) }
})

export function Categories() {
  // Genre → picture of its top anime. Tiles keep their hue gradient until (or unless) it loads.
  const [images, setImages] = useState<Record<string, string>>({})
  useEffect(() => {
    api.getGenreImages().then(setImages).catch(() => {})
  }, [])

  return (
    <div>
      <h1>Categories</h1>

      <section className="category-section">
        <h2>Genres</h2>
        <div className="genre-grid">
          {GENRES.map((genre, i) => (
            <Link
              key={genre}
              to={catalogLink({ genres: genre, sort: 'popular' })}
              className="genre-tile"
              style={
                {
                  '--hue': (i * 360) / GENRES.length,
                  ...(images[genre] && { backgroundImage: `linear-gradient(to top, rgb(0 0 0 / 0.85), rgb(0 0 0 / 0.1)), url("${encodeURI(images[genre])}")` }),
                } as React.CSSProperties
              }
            >
              {genre}
            </Link>
          ))}
        </div>
      </section>

      <section className="category-section">
        <h2>Seasons</h2>
        <div className="chip-row">
          {SEASON_LINKS.map((s) => (
            <Link key={s.label} to={s.to} className="chip">
              {s.label} <span className="muted">{s.sub}</span>
            </Link>
          ))}
        </div>
      </section>

      <section className="category-section">
        <h2>Format</h2>
        <div className="chip-row">
          {Object.entries(FORMATS).map(([value, label]) => (
            <Link key={value} to={catalogLink({ format: value, sort: 'popular' })} className="chip">
              {label}
            </Link>
          ))}
        </div>
      </section>

      <section className="category-section">
        <h2>Status</h2>
        <div className="chip-row">
          {(['RELEASING', 'NOT_YET_RELEASED', 'FINISHED'] as const).map((value) => (
            <Link key={value} to={catalogLink({ status: value, sort: 'popular' })} className="chip">
              {AIRING_STATUSES[value]}
            </Link>
          ))}
        </div>
      </section>
    </div>
  )
}
