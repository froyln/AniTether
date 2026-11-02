import { useLayoutEffect, type FormEvent } from 'react'
import { Navigate, Route, Routes, Link, NavLink, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { api } from './api'
import { useAuth } from './AuthContext'
import { Login } from './pages/Login'
import { MyList } from './pages/MyList'
import { Catalog } from './pages/Catalog'
import { Home } from './pages/Home'
import { Categories } from './pages/Categories'
import { AnimeDetail } from './pages/AnimeDetail'
import { AnimeWatch } from './pages/AnimeWatch'
import { AdminSources } from './pages/AdminSources'
import { AdminHealth } from './pages/AdminHealth'
import { AdminUsers } from './pages/AdminUsers'
import { Settings } from './pages/Settings'
import { PublicProfilePage } from './pages/PublicProfile'

function RequireAuth({ children }: { children: React.ReactNode }) {
  const { user, loading } = useAuth()
  if (loading) return <p>Loading...</p>
  if (!user) return <Navigate to="/login" replace />
  return <>{children}</>
}

function TopBar() {
  const { user, refresh } = useAuth()
  const location = useLocation()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  if (!user) return null

  const q = location.pathname === '/catalog' ? (params.get('q') ?? '') : ''

  // searching from the catalog keeps its other filters (genre, year, ...)
  const search = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const value = String(new FormData(e.currentTarget).get('q') ?? '').trim()
    const next = new URLSearchParams(location.pathname === '/catalog' ? params : undefined)
    next.delete('sort') // relevance vs trending default depends on q
    if (value) next.set('q', value)
    else next.delete('q')
    navigate(`/catalog?${next}`)
  }

  const logout = async () => {
    await api.logout()
    await refresh()
    navigate('/login')
  }

  return (
    <header className="topbar">
      <div className="topbar-inner">
        <Link to="/" className="logo">
          AniTether
        </Link>
        <nav>
          <NavLink to="/" end>
            Home
          </NavLink>
          <NavLink to="/categories">Categories</NavLink>
          <NavLink to="/catalog">Catalog</NavLink>
          <NavLink to="/list">My list</NavLink>
        </nav>
        <form className="topbar-search" role="search" onSubmit={search}>
          <input key={q} name="q" type="search" defaultValue={q} placeholder="Search anime..." aria-label="Search anime" />
        </form>
        {/* keyed on path + hash so the menu closes after navigating */}
        <details className="menu" key={location.pathname + location.hash}>
          <summary aria-label="Account menu">
            {user.avatarUpdatedAt && <img src={`/api/me/avatar?v=${new Date(user.avatarUpdatedAt).getTime()}`} alt="" />}
            {user.displayName.charAt(0).toUpperCase()}
          </summary>
          <div className="menu-panel">
            <span className="menu-name">{user.displayName}</span>
            <Link to="/settings">Settings</Link>
            <Link to="/settings#import">Import list</Link>
            {user.canManageUsers && <Link to="/admin/users">Users</Link>}
            {user.isAdmin && <Link to="/admin/health">Source health (admin)</Link>}
            {user.isAdmin && <Link to="/admin/sources">Sources (admin)</Link>}
            <button type="button" onClick={logout}>
              Log out
            </button>
          </div>
        </details>
      </div>
    </header>
  )
}

function App() {
  const { pathname } = useLocation()

  // BrowserRouter has no scroll restoration: start each page at the top. Layout effect so it
  // runs before a page's own (passive) effects, letting Catalog restore its saved offset.
  useLayoutEffect(() => {
    window.scrollTo(0, 0)
  }, [pathname])

  return (
    <>
      <TopBar />
      <main className="page">
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route path="/u/:id" element={<PublicProfilePage />} />
          <Route path="/my-profile/preview" element={<RequireAuth><PublicProfilePage preview /></RequireAuth>} />
          <Route
            path="/"
            element={
              <RequireAuth>
                <Home />
              </RequireAuth>
            }
          />
          <Route
            path="/categories"
            element={
              <RequireAuth>
                <Categories />
              </RequireAuth>
            }
          />
          <Route
            path="/list"
            element={
              <RequireAuth>
                <MyList />
              </RequireAuth>
            }
          />
          <Route
            path="/catalog"
            element={
              <RequireAuth>
                <Catalog />
              </RequireAuth>
            }
          />
          <Route
            path="/anime/:anilistId"
            element={
              <RequireAuth>
                <AnimeDetail />
              </RequireAuth>
            }
          />
          <Route
            path="/anime/:anilistId/watch/:episode"
            element={
              <RequireAuth>
                <AnimeWatch />
              </RequireAuth>
            }
          />
          <Route path="/search" element={<Navigate to="/catalog" replace />} />
          <Route
            path="/admin/sources"
            element={
              <RequireAuth>
                <AdminSources />
              </RequireAuth>
            }
          />
          <Route
            path="/admin/users"
            element={
              <RequireAuth>
                <AdminUsers />
              </RequireAuth>
            }
          />
          <Route
            path="/admin/health"
            element={
              <RequireAuth>
                <AdminHealth />
              </RequireAuth>
            }
          />
          <Route
            path="/settings"
            element={
              <RequireAuth>
                <Settings />
              </RequireAuth>
            }
          />
          <Route path="/import" element={<Navigate to="/settings#import" replace />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </>
  )
}

export default App
