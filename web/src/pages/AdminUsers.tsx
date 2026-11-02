import { useEffect, useState, type FormEvent } from 'react'
import { useAuth } from '../AuthContext'
import { api, type AdminUser, type Role } from '../api'
import './Settings.css'

const ROLE_LABELS: Record<string, string> = { Admin: 'Admin', Manager: 'Manager', '': 'User' }

/** The server decides who may do what; this page only hides what a Manager
 * can't do (grant roles), it doesn't enforce it. */
export function AdminUsers() {
  const { user } = useAuth()
  const [users, setUsers] = useState<AdminUser[]>([])
  const [error, setError] = useState<string | null>(null)

  const load = () => api.listUsers().then(setUsers)

  useEffect(() => {
    if (user?.canManageUsers) load()
  }, [user])

  if (!user?.canManageUsers) {
    return <p role="alert">Not authorized.</p>
  }

  const assignable = user.isAdmin ? ['', 'Manager', 'Admin'] : ['']

  const run = async (action: () => Promise<unknown>) => {
    setError(null)
    try {
      await action()
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Request failed.')
    }
  }

  const onCreate = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const form = e.currentTarget
    const data = new FormData(form)
    run(async () => {
      await api.createUser({
        email: String(data.get('email')),
        displayName: String(data.get('displayName')),
        password: String(data.get('password')),
        role: (String(data.get('role') ?? '') || null) as Role | null,
      })
      form.reset()
    })
  }

  const resetPassword = (target: AdminUser) => {
    const password = window.prompt(`New password for ${target.email}`)
    if (password) run(() => api.resetUserPassword(target.id, password))
  }

  return (
    <div>
      <h1>Users</h1>
      {error && <p role="alert">{error}</p>}
      <section className="card settings-section">
        <table className="settings-table">
          <thead>
            <tr>
              <th>User</th>
              <th>Role</th>
              <th>Status</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {users.map((u) => (
              <tr key={u.id}>
                <td>
                  {u.displayName}
                  <div className="muted">{u.email}</div>
                </td>
                <td>
                  {user.isAdmin ? (
                    <select
                      value={u.role ?? ''}
                      aria-label={`Role of ${u.email}`}
                      onChange={(e) => run(() => api.updateUser(u.id, { setRole: true, role: (e.target.value || null) as Role | null }))}
                    >
                      {assignable.map((r) => (
                        <option key={r} value={r}>
                          {ROLE_LABELS[r]}
                        </option>
                      ))}
                    </select>
                  ) : (
                    <span className="muted">{ROLE_LABELS[u.role ?? '']}</span>
                  )}
                </td>
                <td className="muted">{u.locked ? 'Locked' : 'Active'}</td>
                <td>
                  <button type="button" onClick={() => resetPassword(u)}>
                    Reset password
                  </button>{' '}
                  <button type="button" className={u.locked ? 'primary' : 'danger'} onClick={() => run(() => api.updateUser(u.id, { locked: !u.locked }))}>
                    {u.locked ? 'Unlock' : 'Lock'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section className="card settings-section admin-form">
        <h2>Create user</h2>
        <form onSubmit={onCreate}>
          <label>
            Email
            <input name="email" type="email" required />
          </label>
          <label>
            Display name
            <input name="displayName" maxLength={50} required />
          </label>
          <label>
            Temporary password
            <input name="password" type="password" autoComplete="new-password" required />
            <small className="muted">6+ characters with upper, lower, digit and symbol. They can change it in Settings.</small>
          </label>
          {assignable.length > 1 && (
            <label>
              Role
              <select name="role" defaultValue="">
                {assignable.map((r) => (
                  <option key={r} value={r}>
                    {ROLE_LABELS[r]}
                  </option>
                ))}
              </select>
            </label>
          )}
          <button type="submit" className="primary">
            Create user
          </button>
        </form>
      </section>
    </div>
  )
}
