import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useLocation } from 'react-router-dom'
import { api, type ImportSummary } from '../api'
import { useAuth } from '../AuthContext'
import { LANGUAGE_LABELS, AUDIO_LABELS } from '../sourceLanguage'
import { THEMES, MODES, applyTheme, currentTheme } from '../theme'
import { avatarImageData } from '../avatar'
import './Settings.css'

const SECTIONS = [
  { id: 'import', label: 'Import list' },
  { id: 'appearance', label: 'Appearance' },
  { id: 'account', label: 'Account' },
]

export function Settings() {
  const { hash } = useLocation()

  // App scrolls to top on navigation; jump to the requested section after that
  useEffect(() => {
    if (hash) document.getElementById(hash.slice(1))?.scrollIntoView()
  }, [hash])

  return (
    <div>
      <h1>Settings</h1>
      <div className="settings-layout">
        <nav className="settings-nav">
          {SECTIONS.map((s) => (
            <a key={s.id} href={`#${s.id}`} className={hash === `#${s.id}` ? 'active' : ''}>
              {s.label}
            </a>
          ))}
        </nav>
        <div className="settings-sections">
          <ImportSection />
          <AppearanceSection />
          <AccountSection />
        </div>
      </div>
    </div>
  )
}

function SummaryView({ summary }: { summary: ImportSummary }) {
  return (
    <p className="import-summary">
      Added <strong>{summary.added}</strong>, updated <strong>{summary.updated}</strong>, skipped{' '}
      <strong>{summary.skipped}</strong>
      {summary.unmatched > 0 && (
        <>
          , <strong>{summary.unmatched}</strong> unmatched
        </>
      )}
      .
    </p>
  )
}

function ImportSection() {
  const [username, setUsername] = useState('')
  const [aniListSummary, setAniListSummary] = useState<ImportSummary | null>(null)
  const [aniListError, setAniListError] = useState<string | null>(null)
  const [malSummary, setMalSummary] = useState<ImportSummary | null>(null)
  const [malError, setMalError] = useState<string | null>(null)
  const [busy, setBusy] = useState<'anilist' | 'mal' | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)

  const onImportAniList = async (e: FormEvent) => {
    e.preventDefault()
    setAniListError(null)
    setAniListSummary(null)
    setBusy('anilist')
    try {
      setAniListSummary(await api.importAniList(username))
    } catch (err) {
      setAniListError(err instanceof Error ? err.message : 'Import failed.')
    } finally {
      setBusy(null)
    }
  }

  const onImportMal = async (e: FormEvent) => {
    e.preventDefault()
    setMalError(null)
    setMalSummary(null)
    const file = fileInput.current?.files?.[0]
    if (!file) {
      setMalError('Choose a file first.')
      return
    }
    setBusy('mal')
    try {
      setMalSummary(await api.importMal(file))
    } catch (err) {
      setMalError(err instanceof Error ? err.message : 'Import failed.')
    } finally {
      setBusy(null)
    }
  }

  return (
    <section id="import" className="card settings-section">
      <h2>Import list</h2>
      <p className="muted">Existing entries are merged: whichever side is further along wins, nothing is lost.</p>

      <h3>From AniList</h3>
      <p className="muted">Your AniList anime list must be public.</p>
      <form onSubmit={onImportAniList} className="inline-form">
        <input value={username} onChange={(e) => setUsername(e.target.value)} placeholder="AniList username" required />
        <button type="submit" disabled={busy !== null}>
          {busy === 'anilist' ? 'Importing…' : 'Import'}
        </button>
      </form>
      {aniListError && <p role="alert">{aniListError}</p>}
      {aniListSummary && <SummaryView summary={aniListSummary} />}

      <h3>From MyAnimeList</h3>
      <p className="muted">
        On myanimelist.net: Profile → List → Export list, then upload the XML file here. MAL ids are matched to
        AniList automatically.
      </p>
      <form onSubmit={onImportMal} className="inline-form">
        <input ref={fileInput} type="file" accept=".xml" required />
        <button type="submit" disabled={busy !== null}>
          {busy === 'mal' ? 'Importing…' : 'Import'}
        </button>
      </form>
      {malError && <p role="alert">{malError}</p>}
      {malSummary && <SummaryView summary={malSummary} />}
    </section>
  )
}

function AppearanceSection() {
  const { user, refresh } = useAuth()
  const [selected, setSelected] = useState(currentTheme)
  const [error, setError] = useState<string | null>(null)
  if (!user) return null

  // Applied at once (and remembered in this browser by theme.js), then saved to the account
  // so it follows the user to other devices.
  const choose = async (theme: string, mode: string) => {
    applyTheme(theme, mode)
    setSelected({ theme, mode })
    setError(null)
    try {
      await api.updatePreferences(user, { theme, colorMode: mode })
      await refresh()
    } catch {
      setError('Applied here, but not saved to your account.')
    }
  }

  return (
    <section id="appearance" className="card settings-section">
      <h2>Appearance</h2>
      <fieldset className="theme-choices">
        <legend>Color</legend>
        {THEMES.map((t) => (
          <label key={t.id} className="theme-choice">
            <input type="radio" name="theme" checked={selected.theme === t.id} onChange={() => choose(t.id, selected.mode)} />
            <span className="theme-swatch" style={{ background: `linear-gradient(135deg, ${t.swatch[0]} 50%, ${t.swatch[1]} 50%)` }} aria-hidden="true" />
            {t.label}
          </label>
        ))}
      </fieldset>
      <fieldset className="theme-choices">
        <legend>Mode</legend>
        {MODES.map((m) => (
          <label key={m.id} className="theme-choice">
            <input type="radio" name="mode" checked={selected.mode === m.id} onChange={() => choose(selected.theme, m.id)} />
            {m.label}
          </label>
        ))}
      </fieldset>
      {error && <p role="alert">{error}</p>}
    </section>
  )
}

function AccountSection() {
  const { user, refresh } = useAuth()
  const [error, setError] = useState<string | null>(null)
  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [bio, setBio] = useState(user?.profileBio ?? '')
  const [busy, setBusy] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)
  useEffect(() => {
    setDisplayName(user?.displayName ?? '')
    setBio(user?.profileBio ?? '')
  }, [user?.id, user?.displayName, user?.profileBio])
  if (!user) return null

  const savePreferences = async (preferredLanguage: string, preferredAudio: string) => {
    setError(null)
    try {
      await api.updatePreferences(user, { preferredLanguage: preferredLanguage || null, preferredAudio: preferredAudio || null })
      await refresh()
    } catch {
      setError('Could not save preference.')
    }
  }

  const saveProfile = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.updateProfile({ displayName, bio, profilePublic: user.profilePublic })
      await refresh()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save profile.')
    } finally { setBusy(false) }
  }

  const updateVisibility = async (profilePublic: boolean) => {
    setError(null)
    try { await api.updateProfile({ displayName, bio, profilePublic }); await refresh() }
    catch { setError('Could not update profile visibility.') }
  }

  const uploadAvatar = async (file?: File) => {
    if (!file) return
    setBusy(true)
    setError(null)
    try {
      const image = await avatarImageData(file)
      await api.uploadAvatar(image.contentType, image.data)
      await refresh()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not upload picture.')
    } finally { setBusy(false); if (fileInput.current) fileInput.current.value = '' }
  }

  const removeAvatar = async () => {
    setBusy(true)
    setError(null)
    try { await api.removeAvatar(); await refresh() }
    catch { setError('Could not remove picture.') }
    finally { setBusy(false) }
  }

  return (
    <section id="account" className="card settings-section">
      <h2>Account</h2>
      <dl className="account-info">
        <dt>Email</dt>
        <dd>{user.email}</dd>
        <dt>Role</dt>
        <dd>{user.isAdmin ? 'Admin' : user.canManageUsers ? 'Manager' : 'User'}</dd>
      </dl>
      <h3>Profile</h3>
      <div className="profile-avatar-editor">
        <img src={user.avatarUpdatedAt ? `/api/me/avatar?v=${encodeURIComponent(user.avatarUpdatedAt)}` : undefined} alt="" />
        <div>
          <button type="button" disabled={busy} onClick={() => fileInput.current?.click()}>{user.avatarUpdatedAt ? 'Change picture' : 'Upload picture'}</button>
          {user.avatarUpdatedAt && <button type="button" disabled={busy} onClick={() => void removeAvatar()}>Remove</button>}
          <input ref={fileInput} type="file" accept="image/png,image/jpeg" hidden onChange={(e) => void uploadAvatar(e.target.files?.[0])} />
          <p className="muted">PNG or JPEG. Cropped to a square; up to 12 MB before resizing.</p>
        </div>
      </div>
      <form onSubmit={saveProfile} className="profile-settings-form">
        <label>Display name<input value={displayName} maxLength={50} required onChange={(e) => setDisplayName(e.target.value)} /></label>
        <label>Short bio<textarea value={bio} maxLength={280} rows={3} onChange={(e) => setBio(e.target.value)} placeholder="A little about your anime taste" /></label>
        <button type="submit" disabled={busy}>Save profile</button>
      </form>
      <label className="profile-visibility">
        <input type="checkbox" checked={user.profilePublic} disabled={busy} onChange={(e) => void updateVisibility(e.target.checked)} />
        Allow anyone with my profile link to see my list
      </label>
      <p className="muted">Your name, picture, bio, anime, status, progress and scores are shared. Notes, email and account settings stay private.</p>
      <label>
        Preferred language for streaming sites
        <select
          value={user.preferredLanguage ?? ''}
          onChange={(e) => savePreferences(e.target.value, user.preferredAudio ?? '')}
        >
          <option value="">Auto (browser)</option>
          {Object.entries(LANGUAGE_LABELS).map(([code, label]) => (
            <option key={code} value={code}>
              {label}
            </option>
          ))}
        </select>
      </label>
      <label>
        Preferred audio for the in-page player
        <select
          value={user.preferredAudio ?? ''}
          onChange={(e) => savePreferences(user.preferredLanguage ?? '', e.target.value)}
        >
          <option value="">Sub (default)</option>
          {Object.entries(AUDIO_LABELS).map(([code, label]) => (
            <option key={code} value={code}>
              {label}
            </option>
          ))}
        </select>
      </label>
      {error && <p role="alert">{error}</p>}
      <PasswordForm />
    </section>
  )
}

function PasswordForm() {
  const [message, setMessage] = useState<string | null>(null)

  const onSubmit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const form = e.currentTarget
    const data = new FormData(form)
    const next = String(data.get('next'))
    if (next !== String(data.get('confirm'))) {
      setMessage('New passwords do not match.')
      return
    }
    try {
      await api.changePassword(String(data.get('current')), next)
      form.reset()
      setMessage('Password changed.')
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Could not change password.')
    }
  }

  return (
    <form onSubmit={onSubmit} className="password-form">
      <h3>Change password</h3>
      <label>
        Current password
        <input name="current" type="password" autoComplete="current-password" required />
      </label>
      <label>
        New password
        <input name="next" type="password" autoComplete="new-password" required />
      </label>
      <label>
        Confirm new password
        <input name="confirm" type="password" autoComplete="new-password" required />
      </label>
      <button type="submit" className="primary">
        Change password
      </button>
      {message && <p role="status">{message}</p>}
    </form>
  )
}
