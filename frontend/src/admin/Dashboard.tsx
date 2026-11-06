import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { TriangleAlert } from 'lucide-react'
import { api, type ActivityItem, type ActivityKind, type DashboardResponse } from '../api/client'
import { Status } from './_status'
import { EmptyState, PageHeader, Stat, StatStrip, StatusPill, type Tone } from './_ui'
import { fmtMoney0, fmtMoney2, relativeTime } from './_utils'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { cn } from '@/lib/utils'

/*
 * Reports › Overview: the money worth chasing, how the system is running, and this month's
 * numbers. The day's work itself is on Today. Quiet stats (see STYLE.md): black numbers, a dot
 * only where a status matters, related numbers in one card.
 */

export default function Dashboard() {
  const { data, error } = useQuery({
    queryKey: ['admin-dashboard'],
    queryFn: () => api.admin.dashboard(),
    refetchInterval: 30_000,
  })

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader title="Overview" subtitle="Money worth chasing and how the system is running." />

      {error ? (
        <Alert variant="destructive"><TriangleAlert /><AlertTitle>Couldn’t load the overview</AlertTitle><AlertDescription>{error instanceof Error ? error.message : 'Load failed.'}</AlertDescription></Alert>
      ) : !data ? (
        <div className="space-y-6">
          <Skeleton className="h-28 rounded-xl" />
          <Skeleton className="h-40 rounded-xl" />
          <Skeleton className="h-28 rounded-xl" />
        </div>
      ) : (
        <Body data={data} />
      )}
    </div>
  )
}

function Body({ data }: { data: DashboardResponse }) {
  const stats = withDefaults(data.stats)
  const activity = data.activity ?? []
  const recentRenewals = data.recentRenewals ?? []
  const health = data.health
  const conv = stats.totalLeads > 0 ? (stats.convertedLeads * 100) / stats.totalLeads : 0
  const mtdRevenue = stats.renewtronDirectRevenue + stats.ontraportRevenue
  const mtdSales = stats.renewtronDirectCount + stats.ontraportCount

  return (
    <div className="space-y-6">
      <Recoverable stats={stats} />

      {health ? <HealthCard health={health} /> : null}

      <section className="space-y-3">
        <h2 className="text-heading">This month</h2>
        <StatStrip cols="grid-cols-2 sm:grid-cols-3 lg:grid-cols-6">
          <Stat label="Revenue" value={fmtMoney2(mtdRevenue)} />
          <Stat label="Sales" value={mtdSales.toLocaleString()} />
          <Stat label="Direct" value={fmtMoney2(stats.renewtronDirectRevenue)} sub={`${stats.renewtronDirectCount} sales`} />
          <Stat label="Ontraport" value={fmtMoney2(stats.ontraportRevenue)} sub={`${stats.ontraportCount} sales`} />
          <Stat label="Lead conversion" value={`${conv.toFixed(1)}%`} sub={`${stats.convertedLeads} of ${stats.totalLeads}`} />
          <Stat label="Average basket" value={fmtMoney0(stats.avgBasket)} sub={`${stats.renewalsCompleted.toLocaleString()} renewals`} />
        </StatStrip>
      </section>

      <div className="grid grid-cols-1 items-start gap-6 lg:grid-cols-2">
        <Card className="gap-0 py-0">
          <CardHeader className="border-b py-4 [.border-b]:pb-4">
            <CardTitle>Activity</CardTitle>
            <CardAction className="text-sm text-muted-foreground">Last 48 hours</CardAction>
          </CardHeader>
          <ActivityFeed items={activity} />
        </Card>

        <Card className="gap-0 py-0">
          <CardHeader className="border-b py-4 [.border-b]:pb-4">
            <CardTitle>Recent renewals</CardTitle>
            <CardAction>
              <Button asChild size="sm" variant="ghost" className="-my-1 text-muted-foreground"><Link to="/admin/renewals">View all</Link></Button>
            </CardAction>
          </CardHeader>
          {recentRenewals.length === 0 ? (
            <p className="px-6 py-8 text-center text-sm text-muted-foreground">No renewals yet.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="pl-6">Business</TableHead>
                  <TableHead>Started</TableHead>
                  <TableHead className="pr-6">Status</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {recentRenewals.map((r) => (
                  <TableRow key={r.id}>
                    <TableCell className="pl-6 font-medium">{r.businessName ?? '—'}</TableCell>
                    <TableCell className="text-muted-foreground tabular-nums">
                      {new Date(r.initiatedAt).toLocaleString(undefined, { month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit' })}
                    </TableCell>
                    <TableCell className="pr-6"><Status kind="renewal" status={r.status} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </Card>
      </div>
    </div>
  )
}

/* ─────────────────── Stats ─────────────────── */

/** The money worth chasing: the total, then each kind with a way into its list. */
function Recoverable({ stats }: { stats: DashboardResponse['stats'] }) {
  const total = stats.abandonedAtPaymentValue + stats.ontraportPipelineValue + stats.failedRenewalValue + stats.pastCustomersDueSoonValue
  const items = stats.abandonedAtPaymentCount + stats.ontraportPipelineCount + stats.failedRenewalCount + stats.pastCustomersDueSoonCount
  const count = (n: number) => (n === 0 ? 'All clear' : `${n.toLocaleString()} item${n === 1 ? '' : 's'}`)
  return (
    <section className="space-y-3">
      <h2 className="text-heading">Worth chasing</h2>
      <StatStrip cols="grid-cols-2 sm:grid-cols-3 lg:grid-cols-5">
        <Stat label="Recoverable revenue" value={total > 0 ? fmtMoney0(total) : '$0'} sub={items === 0 ? 'Nothing to chase' : `across ${items.toLocaleString()} items`} />
        <Stat
          label="Abandoned at payment"
          value={`~${fmtMoney0(stats.abandonedAtPaymentValue)}`}
          sub={count(stats.abandonedAtPaymentCount)}
          to="/admin/leads?outcome=RenewalAvailable"
          hint="Leads found a renewable name but never paid"
        />
        <Stat
          label="Ontraport, due in 30 days"
          value={fmtMoney0(stats.ontraportPipelineValue)}
          sub={count(stats.ontraportPipelineCount)}
          to="/admin/ontraport-sales"
          hint="Paid-in-Ontraport sales that are ready, or nearly ready, to lodge at ASIC"
        />
        <Stat
          label="Failed renewals (30d)"
          value={fmtMoney0(stats.failedRenewalValue)}
          sub={count(stats.failedRenewalCount)}
          dot={stats.failedRenewalCount > 0 ? 'action' : null}
          to="/admin/renewal-orders?view=to-fix"
          hint="Renewals that broke before completion; each is a paying client waiting on a retry"
        />
        <Stat
          label="Past clients due soon"
          value={`~${fmtMoney0(stats.pastCustomersDueSoonValue)}`}
          sub={count(stats.pastCustomersDueSoonCount)}
          to="/admin/renewals"
          hint="1-year renewals from 11–13 months ago, due again in the next 60 days"
        />
      </StatStrip>
    </section>
  )
}

/* ─────────────────── System health ─────────────────── */

/** Display names and staleness thresholds for the Hangfire recurring jobs. */
const jobDisplay: Record<string, { label: string; staleAfterHours: number }> = {
  'renewal-reconciliation':     { label: 'Reconciliation',   staleAfterHours: 26 },
  'ontraport-sales-sync':       { label: 'Ontraport sync',   staleAfterHours: 26 },
  'ontraport-process-renewals': { label: 'Process renewals', staleAfterHours: 26 },
  'bulk-renewal-process':       { label: 'Bulk process',     staleAfterHours: 26 },
  'ontraport-outbox-retry':     { label: 'Outbox retry',     staleAfterHours: 2 },
}

type JobState = 'ok' | 'stale' | 'never'

/** "Is the machine running?": job freshness, queue depth, stuck rows, outbox backlog. */
function HealthCard({ health }: { health: NonNullable<DashboardResponse['health']> }) {
  const jobs = (health.recurringJobs ?? []).map((j) => {
    const display = jobDisplay[j.id] ?? { label: j.id.replace(/-/g, ' '), staleAfterHours: 26 }
    const state: JobState = !j.lastExecution
      ? 'never'
      : Date.now() - new Date(j.lastExecution).getTime() > display.staleAfterHours * 3600_000
        ? 'stale'
        : 'ok'
    return { ...j, ...display, state }
  })
  // "never" doesn't trip the card: right after a deploy every job reads never
  // until its first scheduled run, and that's expected, not an incident.
  const attention = !!health.otpProblem || health.stuckCount > 0 || health.outboxDead > 0 || jobs.some((j) => j.state === 'stale')

  return (
    <section className="space-y-3">
      <h2 className="text-heading">System</h2>
      <Card className="gap-0 overflow-hidden py-0">
        <div className="px-5 py-4">
          <span className="flex items-center gap-2 text-sm font-medium">
            <span aria-hidden className={cn('size-2 rounded-full', attention ? 'bg-warning' : 'bg-success')} />
            {attention ? 'Something needs a look' : 'All systems running'}
          </span>
        </div>

        {/* OTP: without it no renewal can pay ASIC, so it outranks everything else here */}
        {health.otpProblem ? (
          <div className="border-t px-5 py-4">
            <Alert variant="destructive" className="border-destructive/30 bg-destructive/5">
              <TriangleAlert />
              <AlertTitle>Renewals are on hold: the OTP SMS can’t be read</AlertTitle>
              <AlertDescription>
                <p title={health.otpProblem}>
                  {health.otpHeldCount > 0
                    ? `${health.otpHeldCount} ${health.otpHeldCount === 1 ? 'renewal is' : 'renewals are'} waiting; they start within an hour of a fix.`
                    : 'New renewals wait until this is fixed.'}{' '}
                  <Link to="/admin/settings" className="font-medium underline">Fix in Settings</Link>
                </p>
              </AlertDescription>
            </Alert>
          </div>
        ) : null}

        <StatStrip bare cols="grid-cols-3" className="border-t">
          <Stat label="Queue" value={String(health.queueDepth)} sub={health.queueDepth === 1 ? 'job waiting' : 'jobs waiting'} />
          <Stat
            label="Stuck"
            value={String(health.stuckCount)}
            sub={health.stuckCount > 0 ? 'need review' : 'renewals'}
            dot={health.stuckCount > 0 ? 'waiting' : null}
            to={health.stuckCount > 0 ? '/admin/renewals' : undefined}
          />
          <Stat
            label="Outbox"
            value={String(health.outboxPending)}
            sub={health.outboxDead > 0 ? `pending · ${health.outboxDead} dead` : 'pending'}
            dot={health.outboxDead > 0 ? 'waiting' : null}
          />
        </StatStrip>

        {jobs.length > 0 ? (
          <div className="grid grid-cols-2 gap-x-6 gap-y-3 border-t px-5 py-4 sm:grid-cols-3 lg:grid-cols-5">
            {jobs.map((j) => (
              <div key={j.id} className="flex min-w-0 items-start gap-2" title={j.nextExecution ? `Next run: ${new Date(j.nextExecution).toLocaleString()}` : undefined}>
                <span aria-hidden className={cn('mt-1.5 size-1.5 shrink-0 rounded-full', j.state === 'ok' ? 'bg-success' : j.state === 'stale' ? 'bg-warning' : 'bg-muted-foreground/40')} />
                <div className="min-w-0">
                  <div className="truncate text-sm">{j.label}</div>
                  <div className="text-caption text-muted-foreground tabular-nums">
                    {j.lastExecution ? relativeTime(j.lastExecution) : 'never ran'}
                  </div>
                </div>
              </div>
            ))}
          </div>
        ) : null}
      </Card>
    </section>
  )
}

/* ─────────────────── Activity ─────────────────── */

function ActivityFeed({ items }: { items: ActivityItem[] }) {
  if (items.length === 0) {
    return <div className="p-6"><EmptyState title="Nothing in the last 48 hours" /></div>
  }
  return (
    <div className="divide-y">
      {items.map((item, i) => <ActivityRow key={i} item={item} />)}
    </div>
  )
}

function ActivityRow({ item }: { item: ActivityItem }) {
  const tag = activityTag(item.kind)
  return (
    <div className="flex items-start gap-3 px-6 py-3">
      <div className="mt-0.5 w-12 shrink-0 text-caption text-muted-foreground tabular-nums">{relativeTime(item.at)}</div>
      <StatusPill tone={tag.tone} className="shrink-0">{tag.label}</StatusPill>
      <div className="min-w-0 flex-1">
        <div className="truncate text-sm font-medium">{item.label ?? '—'}</div>
        <div className="truncate text-caption text-muted-foreground">
          {item.detail ?? ''}
          {item.amount != null ? <span className="tabular-nums"> · {fmtMoney2(item.amount)}</span> : null}
          {item.source ? <span> · {item.source}</span> : null}
        </div>
      </div>
    </div>
  )
}

function activityTag(kind: ActivityKind): { label: string; tone: Tone } {
  switch (kind) {
    case 'paid':      return { label: 'Paid', tone: 'emerald' }
    case 'lead-warm': return { label: 'Lead', tone: 'amber' }
  }
}

function withDefaults(s: Partial<DashboardResponse['stats']>): DashboardResponse['stats'] {
  return {
    totalSearches: s.totalSearches ?? 0,
    successfulSearches: s.successfulSearches ?? 0,
    renewalsInitiated: s.renewalsInitiated ?? 0,
    renewalsCompleted: s.renewalsCompleted ?? 0,
    renewalsPending: s.renewalsPending ?? 0,
    renewalsFailed: s.renewalsFailed ?? 0,
    totalLeads: s.totalLeads ?? 0,
    convertedLeads: s.convertedLeads ?? 0,
    notDueLeads: s.notDueLeads ?? 0,
    renewtronDirectCount: s.renewtronDirectCount ?? 0,
    renewtronDirectRevenue: s.renewtronDirectRevenue ?? 0,
    ontraportCount: s.ontraportCount ?? 0,
    ontraportRevenue: s.ontraportRevenue ?? 0,
    avgBasket: s.avgBasket ?? 79,
    abandonedAtPaymentCount: s.abandonedAtPaymentCount ?? 0,
    abandonedAtPaymentValue: s.abandonedAtPaymentValue ?? 0,
    ontraportPipelineCount: s.ontraportPipelineCount ?? 0,
    ontraportPipelineValue: s.ontraportPipelineValue ?? 0,
    failedRenewalCount: s.failedRenewalCount ?? 0,
    failedRenewalValue: s.failedRenewalValue ?? 0,
    pastCustomersDueSoonCount: s.pastCustomersDueSoonCount ?? 0,
    pastCustomersDueSoonValue: s.pastCustomersDueSoonValue ?? 0,
  }
}
