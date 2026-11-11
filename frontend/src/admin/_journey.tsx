import { Fragment, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowUpRight, Check, ChevronRight, Loader2, RefreshCw, Send, Undo2 } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type JourneyAction, type JourneyCard, type JourneyEvent, type JourneyRecord } from '../api/client'
import { type Meaning } from './_status'
import { Skeletonizer } from '@/components/skeletonizer'
import { sample } from '@/components/skeleton-samples'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'

/** Where each kind of record lives in the admin. */
const RECORD_PAGE: Record<JourneyRecord, (id: string) => string> = {
  lead: (id) => `/admin/leads/${id}`,
  search: (id) => `/admin/searches/${id}`,
  run: (id) => `/admin/renewals/${id}`,
  order: (id) => `/admin/renewal-orders/${id}`,
  sale: (id) => `/admin/ontraport-sales/${id}`,
  email: (id) => `/admin/emails/${id}`,
}

const RECORD_NAME: Record<JourneyRecord, string> = {
  lead: 'lead', search: 'search', run: 'ASIC run', order: 'paid renewal', sale: 'Ontraport sale', email: 'email',
}

const DOT: Record<Meaning, string> = {
  action: 'bg-destructive',
  waiting: 'bg-warning',
  working: 'bg-info',
  done: 'bg-success',
  quiet: 'bg-muted-foreground/40',
}

/** What the latest line means for whoever is reading. */
const STANDING: Record<Exclude<Meaning, 'action' | 'quiet'>, string> = {
  done: 'Nothing to do.',
  working: 'Renewtron is on it; this updates by itself.',
  waiting: 'Waiting; nothing to do yet.',
}

// Times as the business keeps them: Sydney, whatever the viewer's machine says.
const DAY = new Intl.DateTimeFormat('en-AU', { timeZone: 'Australia/Sydney', weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' })
const TIME = new Intl.DateTimeFormat('en-AU', { timeZone: 'Australia/Sydney', hour: 'numeric', minute: '2-digit' })

type Item = { e: JourneyEvent; key: string }
type Kind = Exclude<JourneyRecord, 'email'>

/**
 * "What happened": everything around this record, oldest first, one sentence a line. Every record
 * page of the same journey (the lead, its search, the paid renewal, its ASIC runs, the Ontraport
 * sale) shows this same list. On top, where things stand. A line opens in place to show its
 * record's key facts and what can be done about it, so reading the story never leaves the page;
 * lines that need someone start open. Runs of quiet lines (emails, notes) fold into one.
 */
export function JourneyTimeline({ kind, id, className }: { kind: Kind; id: string | undefined; className?: string }) {
  const { data, isPending, isError } = useQuery({
    queryKey: ['admin-journey', kind, id],
    queryFn: () => api.admin.journey(kind, id!),
    enabled: !!id,
    // While Renewtron is working on something, follow it.
    refetchInterval: (q) => (q.state.data && standing(q.state.data.events.map((e, i) => ({ e, key: String(i) })))?.e.meaning === 'working' ? 5000 : false),
  })
  const [open, setOpen] = useState<Record<string, boolean>>({})
  const [unfolded, setUnfolded] = useState<Record<string, boolean>>({})

  const shown = data ?? (isPending ? SAMPLE : undefined)
  const items: Item[] = (shown?.events ?? []).map((e, i) => ({ e, key: `${e.recordId}-${e.at}-${i}` }))
  const cards = new Map((shown?.records ?? []).map((c) => [`${c.record}:${c.id}`, c]))
  const isOpen = (it: Item) => open[it.key] ?? it.e.meaning === 'action'
  const toggle = (it: Item) => setOpen((o) => ({ ...o, [it.key]: !isOpen(it) }))
  const reveal = (it: Item) => {
    setOpen((o) => ({ ...o, [it.key]: true }))
    requestAnimationFrame(() => document.getElementById(`journey-${it.key}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' }))
  }

  return (
    <Card className={cn('gap-0 py-0', className)}>
      <CardHeader className="gap-2 border-b py-4">
        <CardTitle>What happened</CardTitle>
        {shown ? <Standing items={items} onReveal={reveal} /> : null}
      </CardHeader>
      <CardContent className="px-0 py-2">
        {!shown || isError ? (
          <p className="px-4 py-3 text-sm text-muted-foreground sm:px-6">The story couldn’t be loaded.</p>
        ) : (
          <Skeletonizer loading={isPending}>
            <ol>{byDay(items).map(([day, dayItems]) => (
              <Fragment key={day}>
                <li className="px-4 pt-3 pb-1 text-caption font-medium text-muted-foreground sm:px-6">{day}</li>
                {segments(dayItems).map((seg) => seg.length > 1 && !unfolded[seg[0].key]
                  ? <Fold key={seg[0].key} items={seg} onOpen={() => setUnfolded((u) => ({ ...u, [seg[0].key]: true }))} />
                  : seg.map((it) => (
                    <Line key={it.key} it={it} open={isOpen(it)} onToggle={() => toggle(it)}
                      card={cards.get(`${it.e.record}:${it.e.recordId}`)} here={it.e.record === kind && it.e.recordId === id} />
                  )))}
              </Fragment>
            ))}</ol>
          </Skeletonizer>
        )}
      </CardContent>
    </Card>
  )
}

/** Where things stand: what needs someone, or else the latest line that isn't just a note. */
function Standing({ items, onReveal }: { items: Item[]; onReveal: (it: Item) => void }) {
  const needs = items.filter((it) => it.e.meaning === 'action')
  if (needs.length > 0) {
    return (
      <div className="space-y-1">
        <p className="text-sm font-medium text-destructive">{needs.length === 1 ? 'Needs you' : `${needs.length} things need you`}</p>
        <ul className="space-y-0.5">
          {needs.map((it) => (
            <li key={it.key}>
              <button type="button" onClick={() => onReveal(it)} className="flex items-start gap-2 text-left text-sm hover:underline">
                <span aria-hidden className={cn('mt-1.5 size-2 shrink-0 rounded-full', DOT.action)} />{it.e.text}
              </button>
            </li>
          ))}
        </ul>
      </div>
    )
  }
  const latest = standing(items)
  if (!latest) return <p className="text-sm text-muted-foreground">Nothing has happened yet.</p>
  return (
    <p className="flex items-start gap-2 text-sm">
      <span aria-hidden className={cn('mt-1.5 size-2 shrink-0 rounded-full', DOT[latest.e.meaning])} />
      <span><span className="font-medium">{latest.e.text}.</span>{' '}
        <span className="text-muted-foreground">{STANDING[latest.e.meaning as keyof typeof STANDING]}</span></span>
    </p>
  )
}

function standing(items: Item[]): Item | undefined {
  for (let i = items.length - 1; i >= 0; i--)
    if (items[i].e.meaning !== 'quiet' && items[i].e.meaning !== 'action') return items[i]
  return undefined
}

const ROW = 'flex w-full items-start gap-3 px-4 py-2 text-left sm:px-6'

function Line({ it, open, onToggle, card, here }: { it: Item; open: boolean; onToggle: () => void; card: JourneyCard | undefined; here: boolean }) {
  const { e } = it
  return (
    <li id={`journey-${it.key}`} className={cn(open && 'bg-muted/30')}>
      <button type="button" onClick={onToggle} aria-expanded={open} className={cn(ROW, 'group transition-colors hover:bg-muted/50')}>
        <span className="w-16 shrink-0 pt-px text-caption text-muted-foreground tabular-nums">{TIME.format(new Date(e.at))}</span>
        <span aria-hidden className={cn('mt-1.5 size-2 shrink-0 rounded-full', DOT[e.meaning])} />
        <span className={cn('min-w-0 flex-1 text-sm', e.meaning === 'quiet' && 'text-muted-foreground')}>{e.text}</span>
        <ChevronRight aria-hidden className={cn('mt-0.5 size-4 shrink-0 text-muted-foreground transition-transform group-hover:text-foreground', open && 'rotate-90')} />
      </button>
      {open ? <Opened e={e} card={card} here={here} /> : null}
    </li>
  )
}

/** A line opened: what went on, the record's key facts, what can be done, and the way to its page. */
function Opened({ e, card, here }: { e: JourneyEvent; card: JourneyCard | undefined; here: boolean }) {
  const facts = (card?.facts ?? []).filter((f) => f.value !== e.detail)
  return (
    <div className="space-y-3 pr-4 pb-3 pl-4 sm:pr-6 sm:pl-[7.5rem]">
      {e.detail ? <p className="rounded-md border bg-background px-3 py-2 text-caption whitespace-pre-wrap [overflow-wrap:anywhere]">{e.detail}</p> : null}
      {card ? (
        <div>
          <p className="text-caption font-medium text-muted-foreground">{card.title}</p>
          {facts.length > 0 ? (
            <dl className="mt-1 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
              {facts.map((f) => (
                <Fragment key={f.label}>
                  <dt className="text-muted-foreground">{f.label}</dt>
                  <dd className={cn('min-w-0 [overflow-wrap:anywhere]', f.mono && 'font-mono text-caption leading-5')}>{f.value}</dd>
                </Fragment>
              ))}
            </dl>
          ) : null}
        </div>
      ) : null}
      <div className="flex flex-wrap items-center gap-2">
        {card?.actions.map((a) => <ActionButton key={a} action={a} card={card} />)}
        <span className="ml-auto text-caption text-muted-foreground">
          {here ? 'You’re on this page'
            : <Link to={RECORD_PAGE[e.record](e.recordId)} className="inline-flex items-center gap-0.5 text-link hover:underline">Open {RECORD_NAME[e.record]}<ArrowUpRight className="size-3.5" /></Link>}
        </span>
      </div>
    </div>
  )
}

const ACTIONS: Record<JourneyAction, { label: string; icon: ReactNode; doing: string; done: string; primary?: boolean }> = {
  retry: { label: 'Retry renewal', icon: <RefreshCw />, doing: 'Retrying renewal…', done: 'Renewal queued for retry.', primary: true },
  'bpay-paid': { label: 'Paid at the bank', icon: <Check />, doing: 'Marking BPAY paid…', done: 'Marked BPAY paid.' },
  'put-back': { label: 'Put back on the queues', icon: <Undo2 />, doing: 'Putting it back…', done: 'Back on the work queues.' },
  resend: { label: 'Send again', icon: <Send />, doing: 'Sending again…', done: 'Sent again.', primary: true },
}

function call(action: JourneyAction, card: JourneyCard): Promise<unknown> {
  switch (action) {
    case 'retry': return api.admin.retryRenewal(card.id)
    case 'bpay-paid': return api.admin.markBpayPaid(card.id, true)
    case 'put-back': return card.record === 'sale' ? api.admin.unmarkSaleHandled(card.id) : api.admin.unmarkRenewalHandled(card.id)
    case 'resend': return api.admin.resendEmail(card.id)
  }
}

function ActionButton({ action, card }: { action: JourneyAction; card: JourneyCard }) {
  const queryClient = useQueryClient()
  const [busy, setBusy] = useState(false)
  const a = ACTIONS[action]
  const run = () => {
    setBusy(true)
    void sileo.promise(call(action, card), {
      loading: { title: a.doing },
      success: (r) => ({ title: (r as { message?: string } | undefined)?.message ?? a.done }),
      error: (err) => ({ title: 'Didn’t go through', description: err instanceof Error ? err.message : undefined }),
    }).catch(() => {}).finally(() => {
      setBusy(false)
      // The story, the record pages and the work queues all move with it.
      void queryClient.invalidateQueries({ predicate: (q) => typeof q.queryKey[0] === 'string' && q.queryKey[0].startsWith('admin-') })
    })
  }
  return (
    <Button size="sm" variant={a.primary ? 'default' : 'outline'} disabled={busy} onClick={run}>
      {busy ? <Loader2 className="animate-spin" /> : a.icon}{a.label}
    </Button>
  )
}

/** Quiet lines in a row, folded to one: they're the story's small print. */
function Fold({ items, onOpen }: { items: Item[]; onOpen: () => void }) {
  const messages = items.every((it) => it.e.record === 'email')
  return (
    <li>
      <button type="button" onClick={onOpen} className={cn(ROW, 'group text-muted-foreground transition-colors hover:bg-muted/50')}>
        <span className="w-16 shrink-0 pt-px text-caption tabular-nums">{TIME.format(new Date(items[0].e.at))}</span>
        <span aria-hidden className={cn('mt-1.5 size-2 shrink-0 rounded-full', DOT.quiet)} />
        <span className="min-w-0 flex-1 text-sm">
          {items.length} {messages ? 'messages sent' : 'smaller updates'}
          <span className="hidden sm:inline"> · {items.map((it) => it.e.text).join(' · ')}</span>
        </span>
        <span className="shrink-0 text-caption group-hover:text-foreground">Show</span>
      </button>
    </li>
  )
}

/** A day's lines, with each run of two or more quiet ones kept together. */
function segments(items: Item[]): Item[][] {
  const out: Item[][] = []
  for (const it of items) {
    const last = out[out.length - 1]
    if (it.e.meaning === 'quiet' && last && last[0].e.meaning === 'quiet') last.push(it)
    else out.push([it])
  }
  return out
}

function byDay(items: Item[]): Array<[string, Item[]]> {
  const days = new Map<string, Item[]>()
  for (const it of items) {
    const day = DAY.format(new Date(it.e.at))
    days.set(day, [...(days.get(day) ?? []), it])
  }
  return [...days]
}

// The story drawn as a skeleton until it arrives.
const SAMPLE: { events: JourneyEvent[]; records: JourneyCard[] } = {
  events: [0, 1, 2, 3].map((i) => ({
    at: sample.daysAgo(3 - i * 0.01), text: `${sample.text(i)} for ${sample.business(i)}`, detail: null,
    meaning: i === 3 ? 'done' : 'working', record: 'run', recordId: sample.id(i),
  })),
  records: [],
}
