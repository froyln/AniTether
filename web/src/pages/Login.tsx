import { useState, type FormEvent } from 'react'
import { useNavigate, Navigate } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../AuthContext'

export function Login() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const { user, loading, refresh } = useAuth()
  const navigate = useNavigate()

  // Already signed in: nothing to do here.
  if (loading) return null
  if (user) return <Navigate to="/" replace />

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    try {
      await api.login(email, password)
      await refresh()
      navigate('/')
    } catch {
      setError('Invalid email or password.')
    }
  }

  return (
    <form onSubmit={onSubmit} className="card auth-card">
      <h1>Log in</h1>
      {error && <p role="alert">{error}</p>}
      <label>
        Email
        <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
      </label>
      <label>
        Password
        <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
      </label>
      <button type="submit">Log in</button>
      <p className="muted">Accounts are created by an admin.</p>
    </form>
  )
}
