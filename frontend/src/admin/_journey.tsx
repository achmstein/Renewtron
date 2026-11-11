import { Fragment } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ArrowUpRight } from 'lucide-react'
import { api, type JourneyEvent, type JourneyRecord } from '../api/client'
import { type Meaning } from './_status'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
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

// Times as the business keeps them: Sydney, whatever the viewer's machine says.
const DAY = new Intl.DateTimeFormat('en-AU', { timeZone: 'Australia/Sydney', weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' })
const TIME = new Intl.DateTimeFormat('en-AU', { timeZone: 'Australia/Sydney', hour: 'numeric', minute: '2-digit' })

/**
 * "What happened": everything around this record, oldest first, one sentence a line. Every record
 * page of the same journey (the lead, its search, the paid renewal, its ASIC runs, the Ontraport
 * sale) shows this same list, so moving between them never changes the story: only which lines
 * are this page's (highlighted). Lines about another record open it.
 */
export function JourneyTimeline({ kind, id, className }: { kind: Exclude<JourneyRecord, 'email'>; id: string | undefined; className?: string }) {
  const { data, isPending, isError } = useQuery({
    queryKey: ['admin-journey', kind, id],
    queryFn: () => api.admin.journey(kind, id!),
    enabled: !!id,
  })

  return (
    <Card className={cn('gap-0 py-0', className)}>
      <CardHeader className="border-b py-4">
        <CardTitle>What happened</CardTitle>
        <CardDescription>The whole story, oldest first. This page’s part is highlighted; the rest opens its own page.</CardDescription>
      </CardHeader>
      <CardContent className="px-0 py-2">
        {isPending ? <Loading /> : isError || !data ? (
          <p className="px-4 py-3 text-sm text-muted-foreground sm:px-6">The story couldn’t be loaded.</p>
        ) : (
          <ol>{byDay(data.events).map(([day, events]) => (
            <Fragment key={day}>
              <li className="px-4 pt-3 pb-1 text-caption font-medium text-muted-foreground sm:px-6">{day}</li>
              {events.map((e, i) => <Line key={`${e.recordId}-${e.at}-${i}`} e={e} here={e.record === kind && e.recordId === id} />)}
            </Fragment>
          ))}</ol>
        )}
      </CardContent>
    </Card>
  )
}

function Line({ e, here }: { e: JourneyEvent; here: boolean }) {
  const body = (
    <>
      <span className="w-16 shrink-0 pt-px text-caption text-muted-foreground tabular-nums">{TIME.format(new Date(e.at))}</span>
      <span aria-hidden className={cn('mt-1.5 size-2 shrink-0 rounded-full', DOT[e.meaning])} />
      <span className="min-w-0 flex-1">
        <span className={cn('text-sm', e.meaning === 'quiet' && !here && 'text-muted-foreground')}>{e.text}</span>
        {e.detail ? <span className="mt-0.5 block text-caption text-muted-foreground [overflow-wrap:anywhere]">{e.detail}</span> : null}
      </span>
      {here
        ? <span className="shrink-0 text-caption text-muted-foreground">this page</span>
        : <span className="flex shrink-0 items-center gap-0.5 text-caption text-muted-foreground group-hover:text-foreground">
            <span className="hidden sm:inline">{RECORD_NAME[e.record]}</span><ArrowUpRight className="size-3.5" />
          </span>}
    </>
  )
  const cls = 'flex items-start gap-3 px-4 py-2 sm:px-6'
  return (
    <li aria-current={here ? 'step' : undefined}>
      {here
        ? <div className={cn(cls, 'border-l-2 border-primary bg-accent/60')}>{body}</div>
        : <Link to={RECORD_PAGE[e.record](e.recordId)} className={cn(cls, 'group border-l-2 border-transparent transition-colors hover:bg-muted/50')}>{body}</Link>}
    </li>
  )
}

function byDay(events: JourneyEvent[]): Array<[string, JourneyEvent[]]> {
  const days = new Map<string, JourneyEvent[]>()
  for (const e of events) {
    const day = DAY.format(new Date(e.at))
    days.set(day, [...(days.get(day) ?? []), e])
  }
  return [...days]
}

function Loading() {
  return (
    <div className="space-y-3 px-4 py-3 sm:px-6">
      {[70, 55, 80, 45].map((w) => (
        <div key={w} className="flex items-center gap-3">
          <Skeleton className="h-3 w-12" />
          <Skeleton className="size-2 rounded-full" />
          <Skeleton className="h-3" style={{ width: `${w}%` }} />
        </div>
      ))}
    </div>
  )
}
