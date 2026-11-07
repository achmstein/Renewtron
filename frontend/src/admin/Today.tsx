import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ChevronRight, CircleCheck, TriangleAlert } from 'lucide-react'
import { api, type TodayGroup, type TodayItem } from '../api/client'
import { Queue, useDoneItems, useRowKeyboard } from './_rows'
import type { Meaning } from './_status'
import { RefreshButton } from './_ui'
import { fmtMoney0 } from './_utils'
import { LinkButton, WorkRow, workKey } from './_work'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible'
import { Skeleton } from '@/components/ui/skeleton'
import { cn } from '@/lib/utils'

/*
 * Today: the operator's home page. Everything that needs a person, sorted by how much it matters:
 *
 *   System problems  stop work for everyone (a banner each, at the top)
 *   Urgent           client money at risk: paid but not renewed, ASIC bills holding a renewal
 *   To do            the day's normal work: key requests, key letters
 *   Review           decisions with no deadline: possible duplicate clients
 *
 * The three tiles count each level and filter the page to it. Within a level the server orders
 * rows by risk. The numbers dashboard is under Reports › Overview.
 */

type Level = 'urgent' | 'todo' | 'review'

const LEVELS: Array<{ key: Level; label: string; hint: string; meaning: Meaning }> = [
  { key: 'urgent', label: 'Urgent', hint: 'Client money at risk', meaning: 'action' },
  { key: 'todo', label: 'To do', hint: 'The day’s work', meaning: 'waiting' },
  { key: 'review', label: 'Review', hint: 'No deadline', meaning: 'quiet' },
]

const LEVEL_OF: Record<TodayGroup['key'], Level> = { retry: 'urgent', check: 'urgent', bpay: 'urgent', refund: 'todo', keys: 'todo', decide: 'review' }

const GROUP_TITLES: Record<TodayGroup['key'], string> = {
  retry: 'Paid: try again',
  check: 'Paid: check at ASIC first',
  refund: 'Paid: renew or refund?',
  bpay: 'ASIC bills to pay',
  keys: 'ASIC keys',
  decide: 'Possible duplicates',
}

const DOT: Record<Meaning, string> = {
  action: 'bg-destructive',
  waiting: 'bg-warning',
  working: 'bg-info',
  done: 'bg-success',
  quiet: 'bg-muted-foreground/40',
}

export default function Today() {
  const { data, error, isFetching, refetch } = useQuery({
    queryKey: ['admin-today'],
    queryFn: () => api.admin.today(),
    refetchInterval: 120_000,
  })
  const [filter, setFilter] = useState<Level | null>(null)
  const { done, mark, undo } = useDoneItems()
  useRowKeyboard(mark)

  const now = new Date()
  const marked = data?.groups.flatMap((g) => g.items).filter((i) => done.has(workKey(i))) ?? []
  const open = data ? data.total - marked.length : 0
  // Open items per group, after the ones marked done on this computer.
  const openIn = (g: TodayGroup) => g.count - g.items.filter((i) => done.has(workKey(i))).length
  const levelCount = (l: Level) => data?.groups.filter((g) => LEVEL_OF[g.key] === l).reduce((n, g) => n + openIn(g), 0) ?? 0
  const urgentMoney = data?.groups.filter((g) => LEVEL_OF[g.key] === 'urgent').reduce((n, g) => n + (g.total ?? 0), 0) ?? 0
  const systemProblems = [
    ...(data?.otpProblem
      ? [{ title: 'Card renewals are on hold', text: 'Renewtron can’t read ASIC’s one-time codes from Ontraport.', detail: data.otpProblem, to: '/admin/settings', action: 'Fix in Settings' }]
      : []),
    ...(data?.emailProblem
      ? [{ title: 'Emails aren’t going out', text: 'Customers aren’t getting Renewtron’s emails.', detail: data.emailProblem, to: '/admin/emails?tab=problem', action: 'See emails' }]
      : []),
    ...(data?.cardProblem
      ? [{ title: 'ASIC is declining our card', text: 'Renewals are paying by BPAY instead. Check the card with the bank.', detail: data.cardProblem, to: '/admin/bpay', action: 'BPAY to pay' }]
      : []),
  ]

  return (
    <div className="mx-auto max-w-5xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <div className="mb-6 flex items-end justify-between gap-4">
        <div>
          <p className="text-caption text-muted-foreground">{now.toLocaleDateString('en-AU', { weekday: 'long', day: 'numeric', month: 'long' })}</p>
          <h1 className="mt-1 text-title">
            {!data ? 'Today' : open <= 0 ? 'All clear for now' : `${open.toLocaleString()} thing${open === 1 ? '' : 's'} need${open === 1 ? 's' : ''} you`}
          </h1>
        </div>
        <RefreshButton onClick={() => void refetch()} busy={isFetching} title="Refresh (keys: ↑ ↓ move, Enter open, D done)" />
      </div>

      {error ? (
        <Alert variant="destructive"><TriangleAlert /><AlertTitle>Couldn’t load today’s work</AlertTitle><AlertDescription>{error.message}</AlertDescription></Alert>
      ) : !data ? (
        <div className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-3">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div>
          {[0, 1].map((i) => <Skeleton key={i} className="h-48 rounded-xl" />)}
        </div>
      ) : (
        <div className="space-y-6">
          {systemProblems.map((p) => (
            <Alert key={p.title} variant="destructive" className="flex flex-wrap items-center gap-3 border-destructive/30 bg-destructive/5 py-3 [&>svg]:translate-y-0">
              <TriangleAlert />
              <div className="min-w-0 flex-1">
                <AlertTitle>{p.title}</AlertTitle>
                <AlertDescription title={p.detail}>{p.text}</AlertDescription>
                {/* Phones can't hover: show the underlying error under the summary. */}
                {p.detail ? <p className="mt-1 text-caption break-words sm:hidden">{p.detail}</p> : null}
              </div>
              <Button asChild size="sm" variant="destructive"><Link to={p.to}>{p.action}</Link></Button>
            </Alert>
          ))}

          <Card className="grid grid-cols-3 gap-0 divide-x py-0">
            {LEVELS.map((l) => {
              const n = levelCount(l.key)
              const active = filter === l.key
              return (
                <button
                  key={l.key}
                  type="button"
                  onClick={() => setFilter(active ? null : l.key)}
                  aria-pressed={active}
                  title={active ? 'Show everything' : `Show only ${l.label.toLowerCase()}`}
                  className={cn('min-w-0 px-3 py-4 text-left transition-colors first:rounded-l-xl last:rounded-r-xl hover:bg-muted/50 sm:px-5', active && 'bg-muted')}
                >
                  <span className="flex items-center gap-2 text-sm text-muted-foreground">
                    <span aria-hidden className={cn('size-2 shrink-0 rounded-full', n ? DOT[l.meaning] : 'bg-muted-foreground/30')} />
                    {l.label}
                  </span>
                  <span className="mt-1 block text-xl font-semibold tracking-tight tabular-nums sm:text-2xl">{n}</span>
                  <span className="block truncate text-caption text-muted-foreground">
                    {n === 0 ? 'All clear' : l.key === 'urgent' && urgentMoney ? `${fmtMoney0(urgentMoney)} waiting on us` : l.hint}
                  </span>
                </button>
              )
            })}
          </Card>

          {LEVELS.filter((l) => !filter || l.key === filter).map((l) => {
            const groups = data.groups.filter((g) => LEVEL_OF[g.key] === l.key && openIn(g) > 0)
            if (groups.length === 0) return null
            return (
              <section key={l.key} className="space-y-3">
                <h2 className="flex flex-wrap items-center gap-x-2 text-heading">
                  <span aria-hidden className={cn('size-2 rounded-full', DOT[l.meaning])} />
                  {l.label}
                  <span className="hidden font-normal text-muted-foreground sm:inline">{l.hint}</span>
                </h2>
                {groups.map((g) => <Group key={g.key} group={g} done={done} tone={l.meaning} open={openIn(g)} />)}
              </section>
            )
          })}

          {open <= 0 ? (
            <Card className="items-center gap-2 py-12 text-center">
              <CircleCheck className="size-8 text-success" />
              <p className="font-medium">Nothing needs you right now</p>
              <p className="text-sm text-muted-foreground">New work shows up here by itself.</p>
            </Card>
          ) : null}

          {marked.length ? <MarkedDone items={marked} undo={undo} /> : null}
        </div>
      )}
    </div>
  )
}

function Group({ group, done, tone, open }: { group: TodayGroup; done: Set<string>; tone: Meaning; open: number }) {
  const items = group.items.filter((i) => !done.has(workKey(i)))
  const hidden = group.count - group.items.length
  return (
    <Queue
      title={GROUP_TITLES[group.key]}
      count={open}
      total={group.total ? fmtMoney0(group.total) : undefined}
      action={<GroupAction group={group} />}
      tone={tone}
    >
      {items.map((item) => <WorkRow key={`${item.kind}-${item.id}`} item={item} />)}
      {hidden > 0 ? (
        <div className="border-t px-4 py-2 text-xs text-muted-foreground">
          Showing the {group.items.length} most urgent of {group.count.toLocaleString()}
        </div>
      ) : null}
    </Queue>
  )
}

/** Items marked done on this computer, kept within reach in case of a slip. */
function MarkedDone({ items, undo }: { items: TodayItem[]; undo: (key: string) => void }) {
  return (
    <Collapsible>
      <Card className="gap-0 py-0">
        <CollapsibleTrigger className="group flex w-full items-center justify-between px-4 py-3 text-left text-sm text-muted-foreground">
          {items.length} marked done
          <ChevronRight className="size-4 transition-transform group-data-[state=open]:rotate-90" />
        </CollapsibleTrigger>
        <CollapsibleContent>
          {items.map((i) => (
            <div key={workKey(i)} className="flex items-center gap-2 border-t pr-4">
              <div className="min-w-0 flex-1 opacity-60"><WorkRow item={i} /></div>
              <Button size="sm" variant="ghost" onClick={() => undo(workKey(i))}>Undo</Button>
            </div>
          ))}
        </CollapsibleContent>
      </Card>
    </Collapsible>
  )
}

/** One way into the full list behind a group; paying bills is the one group action worth a primary button. */
function GroupAction({ group }: { group: TodayGroup }) {
  const to = { retry: '/admin/renewal-orders?view=to-fix', check: '/admin/renewal-orders?view=to-fix', refund: '/admin/renewal-orders?view=decide', bpay: '/admin/bpay', keys: '/admin/asic-key-requests', decide: '/admin/clients/duplicates' }[group.key]
  if (group.key === 'bpay') return <LinkButton to={to} primary>Pay bills</LinkButton>
  return <Button asChild size="sm" variant="ghost" className="text-muted-foreground"><Link to={to}>View all</Link></Button>
}
