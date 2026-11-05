import { useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { type Meaning, type StatusEntry, Status } from './_status'
import { When } from './_rows'

/*
 * The shared shape of a record page (renewal, lead, search, sale, key):
 *
 *   <RecordCrumbs client={…} businessName="OKAFOR PLUMBING" list={{ to: '/admin/renewals', label: 'Renewals' }} here="Renewal of 3 Nov" />
 *   <RecordHeader title="Renewal failed: our card was declined" status={entry}>
 *     <NextStep meaning="action" actions={…}>What happened, in words, and what the next step does.</NextStep>
 *     <StoryStrip steps={…} />
 *   </RecordHeader>
 *   …the page's own sections…
 *   <TechnicalDetail>ids, raw errors, ASIC steps</TechnicalDetail>
 */

/** Where the record sits: its client and business name, or the list it came from when it has no client yet. */
export function RecordCrumbs({ client, businessName, list, here }: {
  client: { id: string; fullName: string } | null | undefined
  businessName?: string | null
  /** The record list, shown when the record isn't on a client yet. */
  list: { to: string; label: string }
  here: string
}) {
  const link = 'text-brand-700 hover:text-brand-900 hover:underline'
  const sep = <span className="text-zinc-300" aria-hidden>›</span>
  return (
    <nav aria-label="Breadcrumb" className="mb-4 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs font-mono text-zinc-500">
      {client ? (
        <>
          <Link to="/admin/clients" className={link}>Clients</Link>{sep}
          <Link to={`/admin/clients/${client.id}`} className={link}>{client.fullName || 'Unnamed client'}</Link>{sep}
          {businessName ? <><Link to={`/admin/clients/${client.id}`} className={link}>{businessName}</Link>{sep}</> : null}
        </>
      ) : (
        <>
          <span>Records</span>{sep}
          <Link to={list.to} className={link}>{list.label}</Link>{sep}
          {businessName ? <><span>{businessName}</span>{sep}</> : null}
        </>
      )}
      <span className="text-zinc-700">{here}</span>
      {!client ? <span className="ml-1 rounded bg-zinc-100 px-1.5 py-0.5 text-zinc-500" title="New records are linked to their client every 15 minutes.">not on a client yet</span> : null}
    </nav>
  )
}

/** The page's headline: what this record is, in words, with its status and the next step below. */
export function RecordHeader({ kicker, title, sub, status, children }: {
  kicker: string
  title: ReactNode
  sub?: ReactNode
  status?: StatusEntry
  children?: ReactNode
}) {
  return (
    <div className="rounded-2xl bg-white p-6 ring-1 ring-zinc-200 shadow-sm">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0 flex-1">
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">{kicker}</div>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight text-zinc-900">{title}</h1>
          {sub ? <div className="mt-1 text-sm text-zinc-500">{sub}</div> : null}
        </div>
        {status ? <Status entry={status} size="lg" /> : null}
      </div>
      {children ? <div className="mt-5 space-y-4">{children}</div> : null}
    </div>
  )
}

const NEXT_STYLE: Record<Meaning, string> = {
  action: 'bg-red-50 ring-red-200 text-red-950',
  waiting: 'bg-amber-50 ring-amber-200 text-amber-950',
  working: 'bg-indigo-50 ring-indigo-200 text-indigo-950',
  done: 'bg-emerald-50 ring-emerald-200 text-emerald-950',
  quiet: 'bg-zinc-50 ring-zinc-200 text-zinc-800',
}

/** What happened and what to do next, coloured by who has to act. */
export function NextStep({ meaning, title, children, actions }: {
  meaning: Meaning
  title: ReactNode
  children?: ReactNode
  actions?: ReactNode
}) {
  return (
    <div className={`flex flex-wrap items-center justify-between gap-3 rounded-xl px-4 py-3 ring-1 ${NEXT_STYLE[meaning]}`}>
      <div className="min-w-0 flex-1 text-sm">
        <div className="font-semibold">{title}</div>
        {children ? <div className="mt-0.5 opacity-80">{children}</div> : null}
      </div>
      {actions ? <div className="flex flex-wrap gap-2">{actions}</div> : null}
    </div>
  )
}

export type StoryStep = {
  label: string
  /** done, failed (action), under way, waiting, or not reached yet (quiet). */
  meaning: Meaning
  at?: string | null
  note?: ReactNode
  /** Opens that step's record; omit for the page you're on. */
  to?: string
  current?: boolean
}

const DOT: Record<Meaning, string> = {
  action: 'bg-red-500 ring-red-100',
  waiting: 'bg-amber-500 ring-amber-100',
  working: 'bg-indigo-500 ring-indigo-100',
  done: 'bg-emerald-500 ring-emerald-100',
  quiet: 'bg-zinc-300 ring-zinc-100',
}

/** The records before and after this one, left to right: visit → paid → renewal → BPAY → key… */
export function StoryStrip({ steps }: { steps: StoryStep[] }) {
  return (
    <ol className="grid gap-px overflow-hidden rounded-lg bg-zinc-200 ring-1 ring-zinc-200" style={{ gridTemplateColumns: `repeat(${steps.length}, minmax(0, 1fr))` }}>
      {steps.map((s, i) => {
        const body = (
          <>
            <div className="flex items-center gap-2">
              <span className={`h-2.5 w-2.5 shrink-0 rounded-full ring-4 ${DOT[s.meaning]}`} />
              <span className={`truncate text-xs font-medium ${s.meaning === 'quiet' ? 'text-zinc-400' : 'text-zinc-900'}`}>{s.label}</span>
            </div>
            <div className="mt-1 truncate text-xxs font-mono text-zinc-500">
              {s.note ?? (s.at ? <When at={s.at} /> : s.meaning === 'quiet' ? 'not yet' : '')}
            </div>
          </>
        )
        const cls = `block min-w-0 px-3 py-2.5 ${s.current ? 'bg-white' : 'bg-zinc-50'}`
        return (
          <li key={i} className="min-w-0" aria-current={s.current ? 'step' : undefined}>
            {s.to ? <Link to={s.to} className={`${cls} hover:bg-white`}>{body}</Link> : <div className={cls}>{body}</div>}
          </li>
        )
      })}
    </ol>
  )
}

/** Ids, raw errors and step logs: kept on the page for digging, closed by default. */
export function TechnicalDetail({ children, defaultOpen = false }: { children: ReactNode; defaultOpen?: boolean }) {
  const [open, setOpen] = useState(defaultOpen)
  return (
    <section className="mt-6 rounded-xl bg-white ring-1 ring-zinc-200 shadow-sm">
      <button type="button" onClick={() => setOpen((o) => !o)} aria-expanded={open}
        className="flex w-full items-center justify-between gap-3 px-5 py-3 text-left">
        <span className="text-sm font-semibold text-zinc-900">Technical detail</span>
        <span className="inline-flex items-center gap-1 text-xs font-mono text-zinc-500">
          {open ? 'hide' : 'show'} {open ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}
        </span>
      </button>
      {open ? <div className="border-t border-zinc-100 p-5">{children}</div> : null}
    </section>
  )
}
