import type { KeyboardEvent, MouseEvent, ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { type Meaning } from './_status'
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

const STRIPE: Record<Meaning, string> = {
  action: 'bg-red-500',
  waiting: 'bg-amber-500',
  working: 'bg-indigo-500',
  done: 'bg-emerald-500',
  quiet: 'bg-zinc-200',
}

/** True when the click landed on something that acts on its own (a link, button or input). */
function fromControl(e: { target: EventTarget | null; currentTarget: EventTarget }) {
  const el = e.target as HTMLElement | null
  const hit = el?.closest('a, button, input, select, textarea, label, [role="button"]')
  return !!hit && hit !== e.currentTarget
}

export function SentenceRow({ meaning, what, why, actions, to }: {
  meaning: Meaning
  /** The sentence: what happened, naming the client. */
  what: ReactNode
  /** Second line: when, how much, why it matters or what happens next. */
  why?: ReactNode
  /** Buttons for the next step; clicks on them don't open the row. */
  actions?: ReactNode
  /** The record the row opens. Clicking anywhere outside a link or button goes there. */
  to?: string
}) {
  const navigate = useNavigate()
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
    : {}
  return (
    <div
      {...open}
      data-meaning={meaning}
      className={`grid grid-cols-[4px_1fr_auto] items-center gap-3 border-b border-zinc-100 py-3 pr-4 last:border-b-0 ${
        to ? 'cursor-pointer hover:bg-zinc-50 focus-visible:bg-zinc-50 focus-visible:outline-none' : ''
      }`}
    >
      <span aria-hidden className={`self-stretch rounded-r ${STRIPE[meaning]}`} />
      <div className="min-w-0">
        <div className="text-sm text-zinc-900">{what}</div>
        {why ? <div className="mt-0.5 text-xs text-zinc-500">{why}</div> : null}
      </div>
      <div className="flex flex-wrap items-center justify-end gap-1.5">{actions}</div>
    </div>
  )
}

/** A titled group of sentence rows: one job, how many, how much, and an action for the whole group. */
export function Queue({ title, count, total, action, children, empty }: {
  title: ReactNode
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
    <section className="overflow-hidden rounded-xl bg-white ring-1 ring-zinc-200 shadow-sm">
      <header className="flex items-center justify-between gap-3 border-b border-zinc-200 px-4 py-2.5">
        <h3 className="text-sm font-semibold text-zinc-900">
          {title}
          {count != null || total ? (
            <span className="ml-2 font-mono text-xs font-normal text-zinc-500 tabular-nums">
              {[count != null ? count.toLocaleString() : null, total].filter(Boolean).join(' · ')}
            </span>
          ) : null}
        </h3>
        {action}
      </header>
      {hasRows ? children : <div className="px-4 py-6 text-center text-sm text-zinc-500">{empty ?? 'Nothing here.'}</div>}
    </section>
  )
}

/** The client's name as a link to their page; the one place a row says who it's for. */
export function ClientName({ id, name }: { id?: string | null; name?: string | null }) {
  const text = name?.trim() || 'Unnamed client'
  if (!id) return <span className="font-medium">{text}</span>
  return (
    <Link to={`/admin/clients/${id}`} className="font-medium text-brand-700 underline decoration-brand-200 underline-offset-2 hover:decoration-brand-500">
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

