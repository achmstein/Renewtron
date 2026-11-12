import { useEffect } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { Button } from '@/components/ui/button'

/*
 * Working a list one record at a time: the list remembers what it showed (this browser tab only),
 * and a record opened from it offers the previous and next ones, so fixing renewals one by one
 * never goes back to the list.
 */

type Queue = { label: string; list: string; ids: string[] }
const KEY = 'renewtron.queue'

/** Called by a list: the records it shows now, in order, and how to get back to it. */
export function rememberQueue(q: Queue) {
  try { sessionStorage.setItem(KEY, JSON.stringify(q)) } catch { /* private window */ }
}

function readQueue(): Queue | null {
  try {
    const raw = sessionStorage.getItem(KEY)
    return raw ? (JSON.parse(raw) as Queue) : null
  } catch { return null }
}

/**
 * "3 of 42 · Too late ‹ ›" on a record opened from a list; nothing when it wasn't. `[` and `]` move
 * too. `to` builds a record's address from its id.
 */
export function QueueNav({ id, to }: { id: string | undefined; to: (id: string) => string }) {
  const navigate = useNavigate()
  const q = readQueue()
  const at = id && q ? q.ids.indexOf(id) : -1
  const prev = q && at > 0 ? q.ids[at - 1] : null
  const next = q && at >= 0 && at < q.ids.length - 1 ? q.ids[at + 1] : null

  useEffect(() => {
    if (at < 0) return
    const onKey = (e: KeyboardEvent) => {
      const el = e.target as HTMLElement | null
      if (e.metaKey || e.ctrlKey || e.altKey || el?.closest('input, textarea, select, [contenteditable="true"], [role="dialog"]')) return
      if (e.key === '[' && prev) navigate(to(prev))
      if (e.key === ']' && next) navigate(to(next))
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [at, prev, next, navigate, to])

  if (!q || at < 0) return null
  return (
    <div className="flex items-center gap-1 text-caption text-muted-foreground">
      <Link to={q.list} className="mr-1 hover:underline">{q.label}</Link>
      <span className="tabular-nums">{at + 1} of {q.ids.length}</span>
      <Button asChild={!!prev} size="sm" variant="ghost" className="size-7 p-0" disabled={!prev} title="Previous ( [ )" aria-label="Previous">
        {prev ? <Link to={to(prev)}><ChevronLeft /></Link> : <ChevronLeft />}
      </Button>
      <Button asChild={!!next} size="sm" variant="ghost" className="size-7 p-0" disabled={!next} title="Next ( ] )" aria-label="Next">
        {next ? <Link to={to(next)}><ChevronRight /></Link> : <ChevronRight />}
      </Button>
    </div>
  )
}
