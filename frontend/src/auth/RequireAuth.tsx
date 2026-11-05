import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from './AuthContext'
import { AppLoading } from '../components/AppLoading'

export default function RequireAuth({ children }: { children: React.ReactNode }) {
  const { user, loading } = useAuth()
  const location = useLocation()

  if (loading) return <AppLoading />
  if (!user) return <Navigate to="/login" replace state={{ from: location }} />
  return <>{children}</>
}
