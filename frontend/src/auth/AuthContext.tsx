import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, type MeResponse } from '../api/client'

interface AuthState {
  user: MeResponse | null
  loading: boolean
  refresh: () => Promise<void>
  logout: () => Promise<void>
}

const AuthCtx = createContext<AuthState | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<MeResponse | null>(null)
  const [loading, setLoading] = useState(true)

  const refresh = async () => {
    try {
      const me = await api.me()
      setUser(me)
    } catch {
      setUser(null)
    } finally {
      setLoading(false)
    }
  }

  const logout = async () => {
    try { await api.logout() } catch { /* ignore */ }
    setUser(null)
  }

  useEffect(() => {
    void refresh()
  }, [])

  return <AuthCtx.Provider value={{ user, loading, refresh, logout }}>{children}</AuthCtx.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthCtx)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}
