import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, type ListStatus, type PublicProfile as Profile } from '../api'
import { useAuth } from '../AuthContext'
import { STATUSES, STATUS_LABELS } from '../status'
import './PublicProfile.css'

export function PublicProfilePage({ preview = false }: { preview?: boolean }) {
  const { id = '' } = useParams()
  const { user } = useAuth()
  const [profile, setProfile] = useState<Profile | null>(null)
  const [missing, setMissing] = useState(false)
  const [status, setStatus] = useState<ListStatus | 'All'>('All')

  useEffect(() => {
    setProfile(null)
    setMissing(false)
    const load = preview ? api.myProfilePreview() : api.publicProfile(id)
    load.then(value => setProfile(value)).catch(() => setMissing(true))
  }, [id, preview])

  const entries = useMemo(() => profile?.entries.filter(entry => status === 'All' || entry.status === status) ?? [], [profile, status])
  if (missing) return <div><h1>Profile unavailable</h1><p>This profile is private or does not exist.</p></div>
  if (!profile) return <p>Loading profile…</p>

  const avatarPath = preview ? '/api/me/avatar' : `/api/profiles/${profile.id}/avatar`
  const avatarVersion = profile.avatarUpdatedAt ? `?v=${profile.avatarUpdatedAt}` : ''
  return <div className="public-profile">
    {preview && <div className="profile-preview-note">Preview {profile.isPublic ? '· this profile is shared' : '· only you can see this'}</div>}
    <header className="public-profile-header">
      <div className="public-profile-avatar">
        <span>{profile.displayName.charAt(0).toUpperCase()}</span>
        {profile.avatarUpdatedAt && <img src={`${avatarPath}${avatarVersion}`} alt="" onError={(e) => { e.currentTarget.hidden = true }} />}
      </div>
      <div><h1>{profile.displayName}'s anime list</h1>{profile.bio && <p>{profile.bio}</p>}</div>
    </header>

    <div className="public-profile-tabs" role="tablist" aria-label="List status">
      <button type="button" className={status === 'All' ? 'active' : ''} aria-selected={status === 'All'} onClick={() => setStatus('All')}>
        All <span>{profile.entries.length}</span>
      </button>
      {STATUSES.map(value => {
        const count = profile.entries.filter(entry => entry.status === value).length
        return <button key={value} type="button" className={status === value ? 'active' : ''} aria-selected={status === value} onClick={() => setStatus(value)}>
          {STATUS_LABELS[value]} <span>{count}</span>
        </button>
      })}
    </div>

    {entries.length === 0 ? <p className="muted">No anime in this section yet.</p> : <div className="public-profile-grid">
      {entries.map(entry => <article className="public-list-card" key={entry.anilistId}>
        <div className="public-list-cover">
          {entry.coverUrl ? <img src={entry.coverUrl} alt="" loading="lazy" /> : <span>{entry.title}</span>}
        </div>
        <div className="public-list-details">
          {user ? <Link to={`/anime/${entry.anilistId}`}>{entry.title}</Link> : <a href={`https://anilist.co/anime/${entry.anilistId}`} target="_blank" rel="noreferrer">{entry.title}</a>}
          <span>{STATUS_LABELS[entry.status]}</span>
          <span>Episode {entry.progress}</span>
          {entry.score != null && <span>Score {entry.score}/10</span>}
        </div>
      </article>)}
    </div>}
  </div>
}
