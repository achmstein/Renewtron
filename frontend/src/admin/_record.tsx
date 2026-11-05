import { Fragment, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ChevronRight } from 'lucide-react'
import { type Meaning, type StatusEntry, Status } from './_status'
import { When } from './_rows'
import {
  Breadcrumb, BreadcrumbItem, BreadcrumbLink, BreadcrumbList, BreadcrumbPage, BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible'
import { cn } from '@/lib/utils'

/*
 * The shared shape of a record page (renewal, lead, search, sale, key):
 *
 *   <RecordCrumbs client={…} businessName="OKAFOR PLUMBING" list={{ to: '/admin/renewals', label: 'Renewals' }} here="Renewal of 3 Nov" />
 *   <RecordHeader title="Renewal failed: card declined" sub="OKAFOR PLUMBING · $299" status={entry}>
 *     <NextStep meaning="action" title="Retry the renewal" actions={…} />
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
  const trail: Array<{ to?: string; label: string }> = client
    ? [
        { to: '/admin/clients', label: 'Clients' },
        { to: `/admin/clients/${client.id}`, label: client.fullName || 'Unnamed client' },
        ...(businessName ? [{ to: `/admin/clients/${client.id}`, label: businessName }] : []),
      ]
    : [{ to: list.to, label: list.label }, ...(businessName ? [{ label: businessName }] : [])]
  return (
    <div className="mb-4 flex flex-wrap items-center gap-2">
      <Breadcrumb>
        <BreadcrumbList>
          {trail.map((t, i) => (
            <Fragment key={i}>
              <BreadcrumbItem>
                {t.to ? <BreadcrumbLink asChild><Link to={t.to}>{t.label}</Link></BreadcrumbLink> : <span>{t.label}</span>}
              </BreadcrumbItem>
              <BreadcrumbSeparator />
            </Fragment>
          ))}
          <BreadcrumbItem><BreadcrumbPage>{here}</BreadcrumbPage></BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      {!client ? <Badge variant="outline" className="text-muted-foreground" title="New records are linked to their client every 15 minutes.">Not on a client yet</Badge> : null}
    </div>
  )
}

/** The page's headline: what this record is, in words, with its status and the next step below. */
export function RecordHeader({ title, sub, status, children }: {
  kicker?: string
  title: ReactNode
  sub?: ReactNode
  status?: StatusEntry
  children?: ReactNode
}) {
  return (
    <Card className="gap-5 p-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0 flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
          {sub ? <div className="mt-1 text-sm text-muted-foreground">{sub}</div> : null}
        </div>
        {status ? <Status entry={status} size="lg" /> : null}
      </div>
      {children ? <div className="space-y-4">{children}</div> : null}
    </Card>
  )
}

const NEXT_STYLE: Record<Meaning, string> = {
  action: 'border-destructive/20 bg-destructive/5',
  waiting: 'border-warning/25 bg-warning/5',
  working: 'border-info/20 bg-info/5',
  done: 'border-success/20 bg-success/5',
  quiet: 'bg-muted/50',
}

/** The next step, coloured by who has to act, with its buttons. Keep it to a title and one line. */
export function NextStep({ meaning, title, children, actions }: {
  meaning: Meaning
  title: ReactNode
  children?: ReactNode
  actions?: ReactNode
}) {
  return (
    <div className={cn('flex flex-wrap items-center justify-between gap-3 rounded-lg border px-4 py-3', NEXT_STYLE[meaning])}>
      <div className="min-w-0 flex-1 text-sm">
        <div className="font-medium">{title}</div>
        {children ? <div className="mt-0.5 text-muted-foreground">{children}</div> : null}
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
  action: 'bg-destructive',
  waiting: 'bg-warning',
  working: 'bg-info',
  done: 'bg-success',
  quiet: 'bg-muted-foreground/30',
}

/** The records before and after this one, left to right: visit → paid → renewal → BPAY → key… */
export function StoryStrip({ steps }: { steps: StoryStep[] }) {
  return (
    <ol className="grid divide-x overflow-hidden rounded-lg border" style={{ gridTemplateColumns: `repeat(${steps.length}, minmax(0, 1fr))` }}>
      {steps.map((s, i) => {
        const body = (
          <>
            <div className="flex items-center gap-2">
              <span aria-hidden className={cn('size-2 shrink-0 rounded-full', DOT[s.meaning])} />
              <span className={cn('truncate text-sm font-medium', s.meaning === 'quiet' && 'text-muted-foreground')}>{s.label}</span>
            </div>
            <div className="mt-0.5 truncate pl-4 text-xs text-muted-foreground">
              {s.note ?? (s.at ? <When at={s.at} /> : s.meaning === 'quiet' ? 'Not yet' : '')}
            </div>
          </>
        )
        const cls = cn('block min-w-0 px-3 py-2.5', s.current ? 'bg-card' : 'bg-muted/40')
        return (
          <li key={i} className="min-w-0" aria-current={s.current ? 'step' : undefined}>
            {s.to ? <Link to={s.to} className={cn(cls, 'transition-colors hover:bg-card')}>{body}</Link> : <div className={cls}>{body}</div>}
          </li>
        )
      })}
    </ol>
  )
}

/** Ids, raw errors and step logs: kept on the page for digging, closed by default. */
export function TechnicalDetail({ children, defaultOpen = false }: { children: ReactNode; defaultOpen?: boolean }) {
  return (
    <Collapsible defaultOpen={defaultOpen} className="mt-6">
      <Card className="gap-0 py-0">
        <CollapsibleTrigger className="group flex w-full items-center justify-between gap-3 px-5 py-3 text-left text-sm font-medium">
          Technical detail
          <ChevronRight className="size-4 text-muted-foreground transition-transform group-data-[state=open]:rotate-90" />
        </CollapsibleTrigger>
        <CollapsibleContent className="border-t p-5">{children}</CollapsibleContent>
      </Card>
    </Collapsible>
  )
}
