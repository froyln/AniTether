import { Link } from 'react-router-dom'
import type { Anime } from '../api'
import { FORMATS } from '../catalogFilters'
import './AnimeCard.css'

type Props = {
  anime: Anime
  inList: boolean
  onAdd: (anilistId: number) => void
}

export function AnimeCard({ anime, inList, onAdd }: Props) {
  const title = anime.titleEnglish ?? anime.titleRomaji
  const meta = [anime.format && (FORMATS[anime.format] ?? anime.format), anime.episodes && !(anime.format === 'MOVIE' && anime.episodes === 1) && `${anime.episodes} eps`]
    .filter(Boolean)
    .join(' · ')

  return (
    <div className="anime-card">
      <Link to={`/anime/${anime.anilistId}`} className="cover" title={title}>
        {anime.coverUrl ? <img src={anime.coverUrl} alt="" loading="lazy" /> : <span className="cover-fallback">{title}</span>}
        {meta && <span className="badge">{meta}</span>}
        {anime.airingStatus === 'NOT_YET_RELEASED' && <span className="badge badge-soon">Coming soon</span>}
      </Link>
      {inList ? (
        <span className="in-list" title="In your list">
          ✓
        </span>
      ) : (
        <button type="button" className="quick-add" onClick={() => onAdd(anime.anilistId)} title="Add to list" aria-label={`Add ${title} to list`}>
          +
        </button>
      )}
      <Link to={`/anime/${anime.anilistId}`} className="anime-card-title">
        {title}
      </Link>
    </div>
  )
}
