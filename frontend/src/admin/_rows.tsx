import { createContext, useContext, useEffect, useState, type KeyboardEvent, type MouseEvent, type ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { type Meaning } from './_status'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { fmtDateTime } from './_utils'

/*
 * Rows written as sentences: the admin's way of listing work. Each row leads with what happened,
 * in words, naming the client; a second line says why it matters or what comes next; actions sit
 * on the right. The stripe on the left is the status meaning (red needs action, amber waiting...).
 *
 *   <Queue title="Paid, not renewed yet" count={5} total="$1,495" action={<Button>Open all</Button>}>
 *     <SentenceRow meaning="action" to={`/admin/renewals/${id}`}
 *       what={<><ClientName id={c.id} name={c.name} />: card declined renewing OKAFOR PLUMBING</>}
 *       why={<><When at={failedAt} /> · $299 · due <DueIn at={dueAt} /></>}
 *       actions={<Button size="sm">Retry</Button>} />
 *   </Queue>
 */

const DOT: Record<Meaning, string> = {
  action: 'bg-destructive',
  waiting: 'bg-warning',
  working: 'bg-info',
  done: 'bg-success',
  quiet: 'bg-muted-foreground/30',
}

/**
 * A queue can colour all its rows by how important the queue is (Today's Urgent / To do / Review),
 * instead of by each record's own status.
 */
const RowTone = createContext<Meaning | null>(null)

/** True when the click landed on something that acts on its own (a link, button or input). */
function fromControl(e: { target: EventTarget | null; currentTarget: EventTarget }) {
  const el = e.target as HTMLElement | null
  const hit = el?.closest('a, button, input, select, textarea, label, [role="button"]')
  return !!hit && hit !== e.currentTarget
}

export function SentenceRow({ meaning, what, why, hint, actions, to, doneKey, lead, selected }: {
  meaning: Meaning
  /** Before the dot: a checkbox, for picking rows to act on together. */
  lead?: ReactNode
  selected?: boolean
  /** The sentence: what happened, naming the client. One short line. */
  what: ReactNode
  /** A few facts: when, how much, when it's due. One short line. */
  why?: ReactNode
  /** The next step or reason, in a sentence: hover text on the sentence, its own line on phones. */
  hint?: string | null
  /** Buttons for the next step; clicks on them don't open the row. */
  actions?: ReactNode
  /** The record the row opens. Clicking anywhere outside a link or button goes there. */
  to?: string
  /** Identifies the row for D (mark done) in useRowKeyboard. */
  doneKey?: string
}) {
  const navigate = useNavigate()
  meaning = useContext(RowTone) ?? meaning
  const open = to
    ? {
        role: 'link' as const,
        tabIndex: 0,
        onClick: (e: MouseEvent<HTMLDivElement>) => {
          if (fromControl(e)) return
          if (e.metaKey || e.ctrlKey) window.open(to, '_blank')
          else navigate(to)
        },
        onKeyDown: (e: KeyboardEvent<HTMLDivElement>) => {
          if (e.key === 'Enter' && !fromControl(e)) navigate(to)
        },
      }
    : { tabIndex: 0 }
  return (
    <div
      {...open}
      data-row=""
      data-done-key={doneKey}
      data-meaning={meaning}
      className={cn(
        'flex flex-wrap items-center gap-x-3 gap-y-2 border-b px-4 py-3 last:border-b-0 outline-none focus-visible:bg-accent sm:flex-nowrap',
        to && 'cursor-pointer hover:bg-muted/50',
        selected && 'bg-accent/60 hover:bg-accent/80',
      )}
    >
      {lead}
      <span aria-hidden className={cn('size-2 shrink-0 self-start mt-1.5 rounded-full', DOT[meaning])} />
      <div className="min-w-0 flex-1">
        <div title={hint ?? undefined} className="line-clamp-2 text-sm [overflow-wrap:anywhere] sm:line-clamp-none sm:truncate">{what}</div>
        {why ? <div className="mt-0.5 line-clamp-2 text-caption text-muted-foreground [overflow-wrap:anywhere] sm:line-clamp-none sm:truncate">{why}</div> : null}
        {hint ? <div className="mt-0.5 text-caption text-muted-foreground sm:hidden">{hint}</div> : null}
      </div>
      {/* Phones: the buttons drop under the sentence (lined up past the dot) rather than squeezing it. */}
      {actions ? <div className="flex w-full flex-wrap items-center gap-1.5 pl-5 sm:w-auto sm:shrink-0 sm:flex-nowrap sm:pl-0">{actions}</div> : null}
    </div>
  )
}

/** A titled group of rows: one job, how many, how much, and an action for the whole group. */
export function Queue({ title, count, total, action, children, empty, tone }: {
  title: ReactNode
  /** Colours every row the same, by the queue's importance. */
  tone?: Meaning
  count?: number
  /** Money in the group, already formatted. */
  total?: string
  action?: ReactNode
  children: ReactNode
  /** Shown instead of rows when the group is clear. */
  empty?: ReactNode
}) {
  const hasRows = Array.isArray(children) ? children.some(Boolean) : !!children
  return (
    <Card className="gap-0 overflow-hidden py-0">
      <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2 border-b px-4 py-3">
        <h3 className="flex flex-wrap items-center gap-2 text-heading">
          {title}
          {count != null ? <Badge variant="secondary" className="tabular-nums">{count.toLocaleString()}</Badge> : null}
          {total ? <span className="text-sm font-normal text-muted-foreground tabular-nums">{total}</span> : null}
        </h3>
        {action}
      </div>
      {hasRows ? <RowTone.Provider value={tone ?? null}>{children}</RowTone.Provider> : <div className="px-4 py-8 text-center text-sm text-muted-foreground">{empty ?? 'All clear.'}</div>}
    </Card>
  )
}

/** The client's name as a link to their page; the one place a row says who it's for. */
export function ClientName({ id, name }: { id?: string | null; name?: string | null }) {
  const text = name?.trim() || 'Unnamed client'
  if (!id) return <span className="font-medium">{text}</span>
  return (
    <Link to={`/admin/clients/${id}`} className="font-medium text-link hover:underline">
      {text}
    </Link>
  )
}

const DAY = 86_400_000

/** Whole calendar days from today to the instant (negative when past), in the viewer's time zone. */
function dayDiff(iso: string): number {
  const d = new Date(iso)
  const a = new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime()
  const now = new Date()
  const b = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime()
  return Math.round((a - b) / DAY)
}

/** "just now", "5 minutes ago", "3 hours ago", "yesterday", "4 days ago", "2 Oct", "2 Oct 2025". */
export function agoWords(iso: string): string {
  const ms = Date.now() - new Date(iso).getTime()
  if (ms < 0) return dueWords(iso)
  const m = Math.floor(ms / 60_000)
  if (m < 1) return 'just now'
  if (m < 60) return `${m} minute${m === 1 ? '' : 's'} ago`
  const h = Math.floor(m / 60)
  if (h < 24 && dayDiff(iso) === 0) return `${h} hour${h === 1 ? '' : 's'} ago`
  const days = -dayDiff(iso)
  if (days <= 1) return 'yesterday'
  if (days < 30) return `${days} days ago`
  return shortDate(iso)
}

/** "today", "tomorrow", "in 9 days" under a month away; "yesterday", "3 days ago" when past; else the date. */
export function dueWords(iso: string): string {
  const days = dayDiff(iso)
  if (days === 0) return 'today'
  if (days === 1) return 'tomorrow'
  if (days === -1) return 'yesterday'
  if (days > 1 && days < 31) return `in ${days} days`
  if (days < -1 && days > -31) return `${-days} days ago`
  return shortDate(iso)
}

function shortDate(iso: string): string {
  const d = new Date(iso)
  const sameYear = d.getFullYear() === new Date().getFullYear()
  return d.toLocaleDateString('en-AU', sameYear ? { day: 'numeric', month: 'short' } : { day: 'numeric', month: 'short', year: 'numeric' })
}

/** A past instant in words ("2 days ago"), with the exact date and time on hover. */
export function When({ at }: { at: string | null | undefined }) {
  if (!at) return <span>—</span>
  return <time dateTime={at} title={fmtDateTime(at)}>{agoWords(at)}</time>
}

/** A due date in words ("in 9 days", "tomorrow"), with the date on hover. */
export function DueIn({ at }: { at: string | null | undefined }) {
  if (!at) return <span>no due date</span>
  return <time dateTime={at} title={new Date(at).toLocaleDateString('en-AU', { weekday: 'short', day: 'numeric', month: 'long', year: 'numeric' })}>{dueWords(at)}</time>
}


/**
 * Keyboard for a list of sentence rows: ↑/↓ (or k/j) move between rows, Enter opens the focused
 * row, D marks it done. Ignored while typing in a field or with a dialog open.
 */
export function useRowKeyboard(onDone?: (key: string) => void) {
  useEffect(() => {
    const onKey = (e: globalThis.KeyboardEvent) => {
      if (e.ctrlKey || e.metaKey || e.altKey) return
      const t = e.target as HTMLElement | null
      if (t?.closest('input, textarea, select, [contenteditable="true"], [role="dialog"]')) return
      const rows = Array.from(document.querySelectorAll<HTMLElement>('[data-row]'))
      if (rows.length === 0) return
      const current = (document.activeElement as HTMLElement | null)?.closest<HTMLElement>('[data-row]') ?? null
      const i = current ? rows.indexOf(current) : -1
      const move = (to: number) => {
        e.preventDefault()
        const row = rows[Math.max(0, Math.min(rows.length - 1, to))]
        row.focus()
        row.scrollIntoView({ block: 'nearest' })
      }
      if (e.key === 'ArrowDown' || e.key === 'j') move(i + 1)
      else if (e.key === 'ArrowUp' || e.key === 'k') move(i < 0 ? 0 : i - 1)
      else if ((e.key === 'd' || e.key === 'D') && current?.dataset.doneKey && onDone) {
        e.preventDefault()
        const next = rows[i + 1] ?? rows[i - 1]
        onDone(current.dataset.doneKey)
        next?.focus()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onDone])
}

const DONE_STORE = 'renewtron.admin.done'

/**
 * Items staff marked done on this computer. The key includes the item's status, so an item that
 * changes (a retry fails again, a bill's transfer is refused) comes back by itself.
 */
export function useDoneItems() {
  const [done, setDone] = useState<Set<string>>(() => {
    try { return new Set(JSON.parse(localStorage.getItem(DONE_STORE) ?? '[]') as string[]) } catch { return new Set() }
  })
  const save = (next: Set<string>) => {
    setDone(next)
    // Keep the list from growing without end: the newest 500 marks.
    localStorage.setItem(DONE_STORE, JSON.stringify([...next].slice(-500)))
  }
  return {
    done,
    mark: (key: string) => save(new Set([...done, key])),
    undo: (key: string) => { const next = new Set(done); next.delete(key); save(next) },
  }
}
