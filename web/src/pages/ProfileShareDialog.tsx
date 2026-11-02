import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../AuthContext'
import './ProfileShareDialog.css'

export function ProfileShareDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const { user, refresh } = useAuth()
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState('')
  const url = user ? `${window.location.origin}/u/${user.id}` : ''

  useEffect(() => {
    if (open) dialog.current?.showModal()
    else if (dialog.current?.open) dialog.current.close()
  }, [open])

  const setVisible = async (visible: boolean) => {
    if (!user) return
    setError('')
    try {
      await api.updateProfile({ displayName: user.displayName, bio: user.profileBio ?? '', profilePublic: visible })
      await refresh()
    } catch { setError('Could not update profile visibility.') }
  }

  const copy = async () => {
    try { await navigator.clipboard.writeText(url); setCopied(true); setTimeout(() => setCopied(false), 1800) }
    catch { setError('Copy failed. Select and copy the profile link above.') }
  }

  if (!user) return null
  return <dialog ref={dialog} className="profile-share-dialog" onCancel={onClose} onClick={(e) => e.target === dialog.current && onClose()}>
    <header><div><h2>Share your profile</h2><p className="muted">Choose when your list can be seen.</p></div>
      <button type="button" className="close" onClick={onClose} aria-label="Close">✕</button></header>
    <label className="profile-visibility"><input type="checkbox" checked={user.profilePublic} onChange={(e) => void setVisible(e.target.checked)} />
      Allow anyone with this link to view my profile
    </label>
    <label>Profile link<input readOnly value={url} onFocus={(e) => e.currentTarget.select()} /></label>
    <div className="profile-share-actions">
      <button type="button" onClick={() => void copy()} disabled={!user.profilePublic}>{copied ? 'Copied' : 'Copy link'}</button>
      {user.profilePublic && <Link to={`/u/${user.id}`} onClick={onClose}>View profile</Link>}
      <Link to="/settings#account" onClick={onClose}>Edit profile details</Link>
      <Link to="/my-profile/preview" onClick={onClose}>Preview</Link>
    </div>
    {!user.profilePublic && <p className="muted">Your profile is private. Turn on sharing before copying a working link.</p>}
    {error && <p role="alert">{error}</p>}
  </dialog>
}
