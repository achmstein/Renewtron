import { useEffect } from 'react'
import { Wordmark } from './Wordmark'
import { cn } from '@/lib/utils'

// Set once the screen has been on show. The sign-in check and the admin code each put up their
// own copy, one after the other; the second must carry straight on rather than fade in again,
// or the screen blinks out and back between them.
let shown = false

/**
 * The full-screen loading state while the admin checks the sign-in and loads its code: the R/
 * breathing, the wordmark, and a thin bar sweeping under it. It fades in after a moment, so a
 * fast load never flashes it.
 */
export function AppLoading() {
  const continuing = shown
  useEffect(() => {
    const t = setTimeout(() => { shown = true }, 200)
    return () => clearTimeout(t)
  }, [])
  return (
    <div role="status" aria-live="polite" className="flex min-h-screen flex-1 items-center justify-center bg-zinc-50 px-6">
      <div className={cn('flex flex-col items-center', !continuing && 'app-loading')}>
        <img src="/favicon.svg" alt="" className="app-loading-logo size-12 rounded-xl shadow-sm" />
        <Wordmark className="mt-5 text-lg" />
        <div className="mt-6 h-0.5 w-40 overflow-hidden rounded-full bg-zinc-200">
          <div className="h-full w-1/3 rounded-full bg-brand-500 animate-[table-loading_1.1s_ease-in-out_infinite]" />
        </div>
        <span className="sr-only">Loading</span>
      </div>
    </div>
  )
}
