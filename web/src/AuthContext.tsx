import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, type User } from './api'
import { effectiveAudio, effectiveLanguage } from './sourceLanguage'
import { applyTheme } from './theme'

type AuthState = {
  user: User | null
  loading: boolean
  language: string
  audio: string
  refresh: () => Promise<void>
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [loading, setLoading] = useState(true)

  const refresh = async () => {
    try {
      const me = await api.me()
      // The account's choice wins over this browser's once one was saved.
      if (me.theme || me.colorMode) applyTheme(me.theme, me.colorMode)
      setUser(me)
    } catch {
      setUser(null)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    refresh()
  }, [])

  const language = effectiveLanguage(user?.preferredLanguage ?? null)
  const audio = effectiveAudio(user?.preferredAudio ?? null)

  return <AuthContext.Provider value={{ user, loading, language, audio, refresh }}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
