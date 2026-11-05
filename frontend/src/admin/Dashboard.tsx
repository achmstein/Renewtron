import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Check } from 'lucide-react'
import { api, type ActivityItem, type ActivityKind, type DashboardResponse } from '../api/client'
import { PageHeader, SectionTitle, StatusPill, Th } from './_ui'
import { fmtMoney0, fmtMoney2, relativeTime } from './_utils'

export default function Dashboard() {
  const { data, error } = useQuery({
    queryKey: ['admin-dashboard'],
    queryFn: () => api.admin.dashboard(),
    refetchInterval: 30_000,
  })

  if (error) {
    return (
      <div className="mx-auto max-w-7xl px-4 py-10 sm:px-6 lg:px-8">
        <div className="rounded-lg border border-destructive/20 bg-destructive/5 p-4 text-sm text-destructive">{error instanceof Error ? error.message : 'Load failed.'}</div>
      </div>
    )
  }
  if (!data) {
    return (
      <div className="mx-auto max-w-7xl px-4 py-10 sm:px-6 lg:px-8">
        <p className="text-sm text-muted-foreground">Loading…</p>
      </div>
    )
  }

  const stats = withDefaults(data.stats)
  const activity = data.activity ?? []
  const recentRenewals = data.recentRenewals ?? []
  const health = data.health
  const conv = stats.totalLeads > 0 ? (stats.convertedLeads * 100) / stats.totalLeads : 0
  const mtdRevenue = stats.renewtronDirectRevenue + stats.ontraportRevenue
  const mtdSales = stats.renewtronDirectCount + stats.ontraportCount

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader kicker="REPORTS" title="Overview" subtitle="Where the revenue is hiding, and how the machine is running. The day’s work is on Today." />

      <Hero stats={stats} />

      {health ? <HealthStrip health={health} /> : null}

      {/* Opportunities — the money worth chasing; the day's work itself is on Today */}
      <div className="mt-10">
        {(() => {
          const actionItems = [
            {
              key: 'abandoned',
              tone: 'emerald' as const,
              kicker: 'WIN-BACK',
              count: stats.abandonedAtPaymentCount,
              value: `~${fmtMoney0(stats.abandonedAtPaymentValue)}`,
              title: 'Abandoned at payment',
              detail: 'Leads found a renewable name but never paid. Hit them with a follow-up.',
              ctaLabel: 'Open leads →',
              to: '/admin/leads?outcome=RenewalAvailable',
            },
            {
              key: 'ontraport',
              tone: 'indigo' as const,
              kicker: 'ONTRAPORT',
              count: stats.ontraportPipelineCount,
              value: fmtMoney0(stats.ontraportPipelineValue),
              title: 'Pipeline due in 30 days',
              detail: 'Paid-in-Ontraport sales that are ready (or nearly ready) to fire at ASIC.',
              ctaLabel: 'View Ontraport →',
              to: '/admin/ontraport-sales',
            },
            {
              key: 'failed',
              tone: 'amber' as const,
              kicker: 'RECOVER',
              count: stats.failedRenewalCount,
              value: fmtMoney0(stats.failedRenewalValue),
              title: 'Failed renewals (30d)',
              detail: 'Renewal attempts that broke before completion. Each is a paying customer waiting on a retry.',
              ctaLabel: 'Renewals to fix →',
              to: '/admin/renewals?view=to-fix',
            },
            {
              key: 'repeat',
              tone: 'zinc' as const,
              kicker: 'REPEAT',
              count: stats.pastCustomersDueSoonCount,
              value: `~${fmtMoney0(stats.pastCustomersDueSoonValue)}`,
              title: 'Past customers due soon',
              detail: '1-year renewals from 11–13 months ago — these come back due in the next 60 days.',
              ctaLabel: 'View renewals →',
              to: '/admin/renewals',
            },
          ]

          const visible = actionItems.filter((item) => item.count > 0)
          const headerRight = visible.length > 0
            ? `${visible.reduce((sum, i) => sum + i.count, 0).toLocaleString()} worth chasing`
            : 'nothing to chase'

          return (
            <>
              <SectionTitle kicker="CHASE" title="Opportunities" right={headerRight} />
              <div className="mt-4">
                {visible.length === 0 ? (
                  <CaughtUp />
                ) : (
                  <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                    {visible.map((item) => (
                      <ActionCard
                        key={item.key}
                        tone={item.tone}
                        kicker={item.kicker}
                        count={item.count}
                        value={item.value}
                        title={item.title}
                        detail={item.detail}
                        ctaLabel={item.ctaLabel}
                        to={item.to}
                      />
                    ))}
                  </div>
                )}
              </div>
            </>
          )
        })()}
      </div>

      {/* MTD summary */}
      <div className="mt-10">
        <div className="rounded-xl border border-border bg-card p-5 shadow-sm flex flex-col">
          <SectionTitle kicker="MTD" title="This month" />
          <div className="mt-4 grid grid-cols-2 sm:grid-cols-4 gap-4">
            <KpiMini label="Revenue" value={fmtMoney2(mtdRevenue)} />
            <KpiMini label="Sales" value={mtdSales.toLocaleString()} />
            <KpiMini label="Direct" value={fmtMoney2(stats.renewtronDirectRevenue)} sub={`${stats.renewtronDirectCount} sales`} />
            <KpiMini label="Ontraport" value={fmtMoney2(stats.ontraportRevenue)} sub={`${stats.ontraportCount} sales`} />
          </div>
          <div className="mt-auto pt-5 border-t border-border grid grid-cols-2 gap-4">
            <KpiMini label="Lead conv." value={`${conv.toFixed(1)}%`} sub={`${stats.convertedLeads} of ${stats.totalLeads}`} />
            <KpiMini label="Avg basket" value={fmtMoney0(stats.avgBasket)} sub={`${stats.renewalsCompleted.toLocaleString()} renewals`} />
          </div>
        </div>
      </div>

      {/* Activity + Recent renewals, 50/50 */}
      <div className="mt-10 grid grid-cols-1 lg:grid-cols-2 gap-6 items-start">
        <div>
          <SectionTitle kicker="LIVE" title="Activity" right={`last 48h`} />
          <div className="mt-4">
            <ActivityFeed items={activity} />
          </div>
        </div>

        <div>
          <SectionTitle kicker="LATEST" title="Recent renewals" right={<Link to="/admin/renewals" className="text-sm font-medium text-link hover:text-link">View all →</Link>} />
          <div className="mt-4 overflow-hidden rounded-xl border border-border bg-card shadow-sm">
            <table className="min-w-full">
              <thead className="bg-muted/50 backdrop-blur">
                <tr>
                  <Th>Business</Th>
                  <Th>Initiated</Th>
                  <Th>Status</Th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {recentRenewals.length === 0 ? (
                  <tr><td colSpan={3} className="px-4 py-8 text-center text-sm text-muted-foreground">No renewals yet.</td></tr>
                ) : recentRenewals.map((r) => (
                  <tr key={r.id} className="hover:bg-muted/50 transition-colors">
                    <td className="px-4 py-3 text-sm font-medium text-foreground">{r.businessName ?? '—'}</td>
                    <td className="px-4 py-3 text-sm text-muted-foreground tabular-nums">{new Date(r.initiatedAt).toLocaleString(undefined, { month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit' })}</td>
                    <td className="px-4 py-3"><RenewalStatusBadge status={r.status} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  )
}

/** Display names and staleness thresholds for the Hangfire recurring jobs. */
const jobDisplay: Record<string, { label: string; staleAfterHours: number }> = {
  'renewal-reconciliation':     { label: 'Reconciliation',   staleAfterHours: 26 },
  'ontraport-sales-sync':       { label: 'Ontraport sync',   staleAfterHours: 26 },
  'ontraport-process-renewals': { label: 'Process renewals', staleAfterHours: 26 },
  'bulk-renewal-process':       { label: 'Bulk process',     staleAfterHours: 26 },
  'ontraport-outbox-retry':     { label: 'Outbox retry',     staleAfterHours: 2 },
}

type JobState = 'ok' | 'stale' | 'never'

/** "Is the machine running?" — job freshness, queue depth, stuck rows, outbox backlog. */
function HealthStrip({ health }: { health: NonNullable<DashboardResponse['health']> }) {
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
    <div className="mt-6 overflow-hidden rounded-xl border border-border bg-card shadow-sm">
      {/* Header — the one-glance verdict */}
      <div className="flex items-center justify-between gap-3 px-5 py-3">
        <div className="flex items-center gap-2.5 min-w-0">
          <span className="relative flex h-2 w-2 shrink-0">
            {attention ? <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-amber-400 opacity-60 motion-reduce:hidden"></span> : null}
            <span className={`relative inline-flex h-2 w-2 rounded-full ${attention ? 'bg-warning' : 'bg-success'}`}></span>
          </span>
          <div className="min-w-0">
            <div className="text-xs font-medium text-muted-foreground">System</div>
            <div className="text-sm font-semibold text-foreground truncate">
              {attention ? 'Something needs a look' : 'All systems running'}
            </div>
          </div>
        </div>
        <StatusPill tone={attention ? 'amber' : 'emerald'}>{attention ? 'ATTENTION' : 'HEALTHY'}</StatusPill>
      </div>

      {/* OTP — without it no renewal can pay ASIC, so it outranks everything else here */}
      {health.otpProblem ? (
        <div className="border-t border-warning/25 bg-warning/5 px-5 py-3 text-sm text-warning">
          <div className="font-semibold">Renewals are on hold: the OTP SMS can't be read</div>
          <div className="mt-0.5 text-warning">{health.otpProblem}</div>
          <div className="mt-1 text-xs text-warning">
            {health.otpHeldCount > 0
              ? `${health.otpHeldCount} ${health.otpHeldCount === 1 ? 'renewal is' : 'renewals are'} waiting and will start within an hour of this being fixed.`
              : 'New renewals will wait, without being started at ASIC, until this is fixed.'}{' '}
            <Link to="/admin/settings" className="font-medium underline">Settings →</Link>
          </div>
        </div>
      ) : null}

      {/* Metrics band */}
      <div className="grid grid-cols-3 divide-x divide-border border-t border-border">
        <HealthMetric
          label="Queue"
          value={String(health.queueDepth)}
          sub={health.queueDepth === 1 ? 'job waiting' : 'jobs waiting'}
        />
        <HealthMetric
          label="Stuck"
          value={String(health.stuckCount)}
          sub={health.stuckCount > 0 ? 'need review →' : 'renewals'}
          warn={health.stuckCount > 0}
          link={health.stuckCount > 0 ? '/admin/renewals' : undefined}
        />
        <HealthMetric
          label="Outbox"
          value={String(health.outboxPending)}
          sub={health.outboxDead > 0 ? `pending · ${health.outboxDead} dead` : 'pending'}
          warn={health.outboxDead > 0}
        />
      </div>

      {/* Recurring jobs — freshness per job */}
      {jobs.length > 0 ? (
        <div className="border-t border-border bg-muted/50 px-5 py-3">
          <div className="grid grid-cols-2 gap-x-6 gap-y-2.5 sm:grid-cols-3 lg:grid-cols-5">
            {jobs.map((j) => (
              <div key={j.id} className="flex items-start gap-2 min-w-0" title={j.nextExecution ? `Next run: ${new Date(j.nextExecution).toLocaleString()}` : undefined}>
                <span className={`mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full ${
                  j.state === 'ok' ? 'bg-success' : j.state === 'stale' ? 'bg-warning' : 'bg-zinc-300'
                }`}></span>
                <div className="min-w-0">
                  <div className="text-xs font-medium text-foreground truncate">{j.label}</div>
                  <div className={`text-xxs font-mono tabular-nums ${j.state === 'stale' ? 'text-warning font-semibold' : 'text-muted-foreground/70'}`}>
                    {j.lastExecution ? relativeTime(j.lastExecution) : 'never ran'}
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  )
}

function HealthMetric({ label, value, sub, warn = false, link }: { label: string; value: string; sub: string; warn?: boolean; link?: string }) {
  const body = (
    <div className="px-5 py-3">
      <div className="text-xs font-medium text-muted-foreground">{label}</div>
      <div className="mt-0.5 flex items-baseline gap-1.5">
        <span className={`text-lg font-semibold tabular-nums leading-none ${warn ? 'text-warning' : 'text-foreground'}`}>{value}</span>
        <span className={`text-xxs font-mono ${warn ? 'text-warning' : 'text-muted-foreground/70'}`}>{sub}</span>
      </div>
    </div>
  )
  return link ? <Link to={link} className="block transition-colors hover:bg-muted/50">{body}</Link> : body
}

function CaughtUp() {
  return (
    <div className="rounded-xl border border-dashed border-brand-200 bg-primary/5/50 p-8 text-center">
      <div className="mx-auto h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
        <Check className="h-5 w-5 text-link" />
      </div>
      <div className="mt-3 text-xs font-medium text-link">All clear</div>
      <h3 className="mt-1 text-base font-semibold text-foreground">Nothing in the queue right now</h3>
      <p className="mt-1 text-sm text-muted-foreground max-w-sm mx-auto">No abandoned leads, stuck renewals, or pending pipeline work. The system is healthy — check back as new activity rolls in.</p>
    </div>
  )
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

function Hero({ stats }: { stats: DashboardResponse['stats'] }) {
  const total = stats.abandonedAtPaymentValue + stats.ontraportPipelineValue + stats.failedRenewalValue + stats.pastCustomersDueSoonValue
  const headline = total > 0 ? fmtMoney0(total) : '$0'
  return (
    <div className="relative overflow-hidden rounded-2xl bg-zinc-950 px-6 py-8 sm:px-10 sm:py-10 ring-1 ring-white/5">
      <div className="absolute inset-0 pointer-events-none" aria-hidden="true">
        <div className="absolute -right-24 -top-24 h-72 w-72 rounded-full bg-primary/10 blur-3xl" />
        <div className="absolute -left-24 -bottom-24 h-72 w-72 rounded-full bg-brand-400/5 blur-3xl" />
      </div>
      <div className="relative grid grid-cols-1 lg:grid-cols-3 gap-6 items-center">
        <div className="lg:col-span-2">
          <div className="text-xs font-medium text-brand-400">Recoverable revenue</div>
          <div className="mt-2 font-display text-5xl sm:text-6xl font-bold tracking-tight text-white tabular-nums">{headline}</div>
          <p className="mt-3 max-w-xl text-sm text-muted-foreground/50">
            Across {stats.abandonedAtPaymentCount + stats.ontraportPipelineCount + stats.failedRenewalCount + stats.pastCustomersDueSoonCount} leads, paid-but-pending Ontraport sales, failed renewals, and customers due to renew again — sitting in the action queue below.
          </p>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <HeroStat label="Abandoned" value={`~${fmtMoney0(stats.abandonedAtPaymentValue)}`} count={stats.abandonedAtPaymentCount} />
          <HeroStat label="Pipeline" value={fmtMoney0(stats.ontraportPipelineValue)} count={stats.ontraportPipelineCount} />
          <HeroStat label="Failed" value={fmtMoney0(stats.failedRenewalValue)} count={stats.failedRenewalCount} />
          <HeroStat label="Repeat" value={`~${fmtMoney0(stats.pastCustomersDueSoonValue)}`} count={stats.pastCustomersDueSoonCount} />
        </div>
      </div>
    </div>
  )
}

function HeroStat({ label, value, count }: { label: string; value: string; count: number }) {
  return (
    <div className="rounded-lg bg-card/5 ring-1 ring-white/10 px-3 py-2.5">
      <div className="text-xs text-muted-foreground/70">{label}</div>
      <div className="mt-0.5 text-lg font-semibold text-white tabular-nums">{value}</div>
      <div className="text-xxs font-mono text-muted-foreground tabular-nums">{count} item{count === 1 ? '' : 's'}</div>
    </div>
  )
}

type Tone = 'emerald' | 'indigo' | 'amber' | 'red' | 'zinc'

const toneStyles: Record<Tone, { kicker: string; ring: string; cta: string; dot: string }> = {
  emerald: { kicker: 'text-link', ring: 'hover:ring-primary/20', cta: 'text-link hover:text-link', dot: 'bg-primary' },
  indigo:  { kicker: 'text-info',  ring: 'hover:ring-info/20',  cta: 'text-info hover:text-info',   dot: 'bg-info' },
  amber:   { kicker: 'text-warning',   ring: 'hover:ring-warning/25',   cta: 'text-warning hover:text-warning',     dot: 'bg-warning' },
  red:     { kicker: 'text-destructive',     ring: 'hover:ring-destructive/20',     cta: 'text-destructive hover:text-destructive',         dot: 'bg-destructive' },
  zinc:    { kicker: 'text-muted-foreground',    ring: 'hover:ring-border',    cta: 'text-foreground hover:text-foreground',       dot: 'bg-zinc-400' },
}

function ActionCard({ tone, kicker, count, value, title, detail, ctaLabel, to, hideValue, countPrefix }: {
  tone: Tone
  kicker: string
  count: number
  value: string
  title: string
  detail: string
  ctaLabel: string
  to: string
  hideValue?: boolean
  countPrefix?: string
}) {
  const t = toneStyles[tone]
  return (
    <Link
      to={to}
      className={`group relative flex flex-col rounded-xl bg-card p-5 ring-1 ring-border ${t.ring} hover:shadow-md transition-all`}
    >
      <div className="flex items-center justify-between">
        <div className={`text-xxs font-mono font-medium uppercase tracking-[0.16em] ${t.kicker} flex items-center gap-1.5`}>
          <span className={`h-1.5 w-1.5 rounded-full ${t.dot}`}></span>
          {kicker}
        </div>
        {!hideValue && value ? (
          <div className="text-sm font-semibold text-foreground tabular-nums">{value}</div>
        ) : null}
      </div>
      <div className="mt-3 flex items-baseline gap-2">
        <div className="font-display text-3xl font-bold text-foreground tabular-nums">{countPrefix ?? ''}{count.toLocaleString()}</div>
        <div className="text-sm font-medium text-foreground">{title}</div>
      </div>
      <p className="mt-2 text-sm text-muted-foreground leading-relaxed">{detail}</p>
      <div className={`mt-4 text-sm font-medium ${t.cta}`}>{ctaLabel}</div>
    </Link>
  )
}

function ActivityFeed({ items }: { items: ActivityItem[] }) {
  if (items.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-border bg-card/60 p-6 text-center">
        <p className="text-sm text-muted-foreground">No activity in the last 48 hours.</p>
      </div>
    )
  }
  return (
    <div className="rounded-xl border border-border bg-card shadow-sm divide-y divide-border">
      {items.map((item, i) => (
        <ActivityRow key={i} item={item} />
      ))}
    </div>
  )
}

function ActivityRow({ item }: { item: ActivityItem }) {
  const tag = activityTag(item.kind)
  return (
    <div className="flex items-start gap-3 px-4 py-3">
      <div className="mt-0.5 shrink-0 text-xs font-medium tabular-nums text-muted-foreground/70 w-12">
        {relativeTime(item.at)}
      </div>
      <div className={`mt-0.5 shrink-0 inline-flex items-center rounded px-1.5 py-0.5 text-xxs font-mono font-medium tracking-[0.12em] ${tag.cls}`}>
        {tag.label}
      </div>
      <div className="min-w-0 flex-1">
        <div className="text-sm font-medium text-foreground truncate">{item.label ?? '—'}</div>
        <div className="text-xs text-muted-foreground truncate">
          {item.detail ?? ''}
          {item.amount != null ? <span className="font-mono tabular-nums"> · {fmtMoney2(item.amount)}</span> : null}
          {item.source ? <span className="font-mono"> · {item.source}</span> : null}
        </div>
      </div>
    </div>
  )
}

function activityTag(kind: ActivityKind) {
  switch (kind) {
    case 'paid':      return { label: 'PAID',     cls: 'bg-primary/5 text-link ring-1 ring-primary/20' }
    case 'lead-warm': return { label: 'LEAD',     cls: 'bg-warning/5 text-warning ring-1 ring-warning/25' }
  }
}

function KpiMini({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <div>
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="mt-1 text-base font-semibold text-foreground tabular-nums">{value}</div>
      {sub ? <div className="text-xs text-muted-foreground tabular-nums">{sub}</div> : null}
    </div>
  )
}

function RenewalStatusBadge({ status }: { status: string }) {
  const map: Record<string, string> = {
    Completed:  'bg-primary/5 text-link ring-1 ring-primary/20',
    Processing: 'bg-info/5 text-info ring-1 ring-info/20',
    Pending:    'bg-warning/5 text-warning ring-1 ring-warning/25',
    Failed:     'bg-destructive/5 text-destructive ring-1 ring-destructive/20',
  }
  const cls = map[status] ?? 'bg-muted text-foreground ring-1 ring-border'
  return <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-xxs font-mono font-medium tracking-[0.12em] ${cls}`}>{status.toUpperCase()}</span>
}
