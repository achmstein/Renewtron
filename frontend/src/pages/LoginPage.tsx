import { useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'

export default function LoginPage() {
  const navigate = useNavigate()
  const location = useLocation() as { state?: { from?: { pathname?: string } } }
  const auth = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [rememberMe, setRememberMe] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      await api.login(email, password)
      await auth.refresh()
      const dest = location.state?.from?.pathname ?? '/admin'
      navigate(dest, { replace: true })
    } catch {
      setError('Invalid login attempt.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="flex min-h-screen flex-col justify-center bg-zinc-50 px-4 py-12 sm:px-6 lg:px-8">
      <div className="mx-auto w-full max-w-md">
        <div className="flex justify-center">
          <Wordmark />
        </div>
        <div className="mt-2 text-center text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">
          OPS · SIGN IN
        </div>
        <h1 className="mt-3 text-center text-2xl font-semibold tracking-tight text-zinc-900">
          Sign in to the operator console
        </h1>
      </div>

      <div className="mx-auto mt-8 w-full max-w-md">
        <div className="rounded-2xl bg-white p-8 ring-1 ring-zinc-200 shadow-sm">
          <form onSubmit={submit} className="space-y-5">
            {error ? (
              <div className="rounded-lg bg-red-50 ring-1 ring-red-100 p-3">
                <div className="flex items-start gap-2.5">
                  <svg className="mt-0.5 h-4 w-4 shrink-0 text-red-600" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
                  </svg>
                  <p className="text-sm text-red-900 font-mono">{error}</p>
                </div>
              </div>
            ) : null}

            <div>
              <label htmlFor="email" className="block text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500">
                Email
              </label>
              <input
                id="email"
                type="email"
                autoComplete="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className="mt-1.5 block w-full rounded-md border border-zinc-300 bg-white px-3 py-2 text-sm text-zinc-900 placeholder:text-zinc-400 shadow-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
              />
            </div>

            <div>
              <label htmlFor="password" className="block text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500">
                Password
              </label>
              <input
                id="password"
                type="password"
                autoComplete="current-password"
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="mt-1.5 block w-full rounded-md border border-zinc-300 bg-white px-3 py-2 text-sm text-zinc-900 placeholder:text-zinc-400 shadow-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20"
              />
            </div>

            <div className="flex items-center justify-between">
              <label htmlFor="remember-me" className="inline-flex items-center gap-2 text-sm text-zinc-700">
                <input
                  id="remember-me"
                  type="checkbox"
                  checked={rememberMe}
                  onChange={(e) => setRememberMe(e.target.checked)}
                  className="size-4 rounded border-zinc-300 text-brand-600 focus:ring-brand-500"
                />
                Remember me
              </label>
              <Link to="/" className="text-xs font-mono uppercase tracking-[0.14em] text-brand-700 hover:text-brand-800">
                Forgot password?
              </Link>
            </div>

            <button
              type="submit"
              disabled={loading}
              className="inline-flex w-full items-center justify-center rounded-md bg-brand-600 px-3 py-2 text-sm font-medium text-white shadow-sm transition hover:bg-brand-700 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {loading ? 'Signing in…' : 'Sign in'}
            </button>
          </form>
        </div>

        <p className="mt-6 text-center text-xxs font-mono uppercase tracking-[0.14em] text-zinc-400">
          Restricted · authorised personnel only
        </p>
      </div>
    </div>
  )
}

function Wordmark() {
  return (
    <span className="font-display font-bold text-lg tracking-[0.04em] text-zinc-900 leading-none inline-flex items-baseline">
      <span>RENEW</span>
      <span className="mx-1.5 font-normal text-zinc-300">/</span>
      <span className="text-brand-600">TRON</span>
    </span>
  )
}
