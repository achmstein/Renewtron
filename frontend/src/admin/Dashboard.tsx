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
        <div className="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">{error instanceof Error ? error.message : 'Load failed.'}</div>
      </div>
    )
  }
  if (!data) {
    return (
      <div className="mx-auto max-w-7xl px-4 py-10 sm:px-6 lg:px-8">
        <p className="text-sm text-zinc-500">Loading…</p>
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
      <PageHeader kicker="OPS" title="Today's opportunity" subtitle="Where the revenue is hiding right now — and what to do about it." />

      <Hero stats={stats} />

      {health ? <HealthStrip health={health} /> : null}

      {/* Action queue — full width */}
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
              ctaLabel: 'Retry queue →',
              to: '/admin/renewals?status=Failed',
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
            ? `${visible.reduce((sum, i) => sum + i.count, 0).toLocaleString()} items need attention`
            : 'all clear'

          return (
            <>
              <SectionTitle kicker="ACT" title="Action queue" right={headerRight} />
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
        <div className="rounded-xl border border-zinc-200 bg-white p-5 shadow-sm flex flex-col">
          <SectionTitle kicker="MTD" title="This month" />
          <div className="mt-4 grid grid-cols-2 sm:grid-cols-4 gap-4">
            <KpiMini label="Revenue" value={fmtMoney2(mtdRevenue)} />
            <KpiMini label="Sales" value={mtdSales.toLocaleString()} />
            <KpiMini label="Direct" value={fmtMoney2(stats.renewtronDirectRevenue)} sub={`${stats.renewtronDirectCount} sales`} />
            <KpiMini label="Ontraport" value={fmtMoney2(stats.ontraportRevenue)} sub={`${stats.ontraportCount} sales`} />
          </div>
          <div className="mt-auto pt-5 border-t border-zinc-100 grid grid-cols-2 gap-4">
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
          <SectionTitle kicker="LATEST" title="Recent renewals" right={<Link to="/admin/renewals" className="text-sm font-medium text-brand-700 hover:text-brand-800">View all →</Link>} />
          <div className="mt-4 overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-sm">
            <table className="min-w-full">
              <thead className="bg-zinc-50/80 backdrop-blur">
                <tr>
                  <Th>Business</Th>
                  <Th>Initiated</Th>
                  <Th>Status</Th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-100">
                {recentRenewals.length === 0 ? (
                  <tr><td colSpan={3} className="px-4 py-8 text-center text-sm text-zinc-500">No renewals yet.</td></tr>
                ) : recentRenewals.map((r) => (
                  <tr key={r.id} className="hover:bg-zinc-50 transition-colors">
                    <td className="px-4 py-3 text-sm font-medium text-zinc-900">{r.businessName ?? '—'}</td>
                    <td className="px-4 py-3 text-sm text-zinc-500 tabular-nums">{new Date(r.initiatedAt).toLocaleString(undefined, { month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit' })}</td>
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
  const attention = health.stuckCount > 0 || health.outboxDead > 0 || jobs.some((j) => j.state === 'stale')

  return (
    <div className="mt-6 overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-sm">
      {/* Header — the one-glance verdict */}
      <div className="flex items-center justify-between gap-3 px-5 py-3">
        <div className="flex items-center gap-2.5 min-w-0">
          <span className="relative flex h-2 w-2 shrink-0">
            {attention ? <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-amber-400 opacity-60 motion-reduce:hidden"></span> : null}
            <span className={`relative inline-flex h-2 w-2 rounded-full ${attention ? 'bg-amber-500' : 'bg-emerald-500'}`}></span>
          </span>
          <div className="min-w-0">
            <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500">System</div>
            <div className="text-sm font-semibold text-zinc-900 truncate">
              {attention ? 'Something needs a look' : 'All systems running'}
            </div>
          </div>
        </div>
        <StatusPill tone={attention ? 'amber' : 'emerald'}>{attention ? 'ATTENTION' : 'HEALTHY'}</StatusPill>
      </div>

      {/* Metrics band */}
      <div className="grid grid-cols-3 divide-x divide-zinc-100 border-t border-zinc-100">
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
        <div className="border-t border-zinc-100 bg-zinc-50/50 px-5 py-3">
          <div className="grid grid-cols-2 gap-x-6 gap-y-2.5 sm:grid-cols-3 lg:grid-cols-5">
            {jobs.map((j) => (
              <div key={j.id} className="flex items-start gap-2 min-w-0" title={j.nextExecution ? `Next run: ${new Date(j.nextExecution).toLocaleString()}` : undefined}>
                <span className={`mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full ${
                  j.state === 'ok' ? 'bg-emerald-500' : j.state === 'stale' ? 'bg-amber-500' : 'bg-zinc-300'
                }`}></span>
                <div className="min-w-0">
                  <div className="text-xs font-medium text-zinc-700 truncate">{j.label}</div>
                  <div className={`text-xxs font-mono tabular-nums ${j.state === 'stale' ? 'text-amber-700 font-semibold' : 'text-zinc-400'}`}>
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
      <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500">{label}</div>
      <div className="mt-0.5 flex items-baseline gap-1.5">
        <span className={`text-lg font-semibold tabular-nums leading-none ${warn ? 'text-amber-700' : 'text-zinc-900'}`}>{value}</span>
        <span className={`text-xxs font-mono ${warn ? 'text-amber-700' : 'text-zinc-400'}`}>{sub}</span>
      </div>
    </div>
  )
  return link ? <Link to={link} className="block transition-colors hover:bg-zinc-50">{body}</Link> : body
}

function CaughtUp() {
  return (
    <div className="rounded-xl border border-dashed border-brand-200 bg-brand-50/50 p-8 text-center">
      <div className="mx-auto h-10 w-10 rounded-full bg-brand-100 flex items-center justify-center">
        <Check className="h-5 w-5 text-brand-600" />
      </div>
      <div className="mt-3 text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">All clear</div>
      <h3 className="mt-1 text-base font-semibold text-zinc-900">Nothing in the queue right now</h3>
      <p className="mt-1 text-sm text-zinc-500 max-w-sm mx-auto">No abandoned leads, stuck renewals, or pending pipeline work. The system is healthy — check back as new activity rolls in.</p>
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
        <div className="absolute -right-24 -top-24 h-72 w-72 rounded-full bg-brand-500/10 blur-3xl" />
        <div className="absolute -left-24 -bottom-24 h-72 w-72 rounded-full bg-brand-400/5 blur-3xl" />
      </div>
      <div className="relative grid grid-cols-1 lg:grid-cols-3 gap-6 items-center">
        <div className="lg:col-span-2">
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-400">Recoverable revenue</div>
          <div className="mt-2 font-display text-5xl sm:text-6xl font-bold tracking-tight text-white tabular-nums">{headline}</div>
          <p className="mt-3 max-w-xl text-sm text-zinc-300">
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
    <div className="rounded-lg bg-white/5 ring-1 ring-white/10 px-3 py-2.5">
      <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-400">{label}</div>
      <div className="mt-0.5 text-lg font-semibold text-white tabular-nums">{value}</div>
      <div className="text-xxs font-mono text-zinc-500 tabular-nums">{count} item{count === 1 ? '' : 's'}</div>
    </div>
  )
}

type Tone = 'emerald' | 'indigo' | 'amber' | 'red' | 'zinc'

const toneStyles: Record<Tone, { kicker: string; ring: string; cta: string; dot: string }> = {
  emerald: { kicker: 'text-brand-700', ring: 'hover:ring-brand-200', cta: 'text-brand-700 hover:text-brand-800', dot: 'bg-brand-500' },
  indigo:  { kicker: 'text-indigo-700',  ring: 'hover:ring-indigo-200',  cta: 'text-indigo-700 hover:text-indigo-800',   dot: 'bg-indigo-500' },
  amber:   { kicker: 'text-amber-700',   ring: 'hover:ring-amber-200',   cta: 'text-amber-700 hover:text-amber-800',     dot: 'bg-amber-500' },
  red:     { kicker: 'text-red-700',     ring: 'hover:ring-red-200',     cta: 'text-red-700 hover:text-red-800',         dot: 'bg-red-500' },
  zinc:    { kicker: 'text-zinc-600',    ring: 'hover:ring-zinc-300',    cta: 'text-zinc-700 hover:text-zinc-900',       dot: 'bg-zinc-400' },
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
      className={`group relative flex flex-col rounded-xl bg-white p-5 ring-1 ring-zinc-200 ${t.ring} hover:shadow-md transition-all`}
    >
      <div className="flex items-center justify-between">
        <div className={`text-xxs font-mono font-medium uppercase tracking-[0.16em] ${t.kicker} flex items-center gap-1.5`}>
          <span className={`h-1.5 w-1.5 rounded-full ${t.dot}`}></span>
          {kicker}
        </div>
        {!hideValue && value ? (
          <div className="text-sm font-semibold text-zinc-900 tabular-nums">{value}</div>
        ) : null}
      </div>
      <div className="mt-3 flex items-baseline gap-2">
        <div className="font-display text-3xl font-bold text-zinc-900 tabular-nums">{countPrefix ?? ''}{count.toLocaleString()}</div>
        <div className="text-sm font-medium text-zinc-700">{title}</div>
      </div>
      <p className="mt-2 text-sm text-zinc-500 leading-relaxed">{detail}</p>
      <div className={`mt-4 text-sm font-medium ${t.cta}`}>{ctaLabel}</div>
    </Link>
  )
}

function ActivityFeed({ items }: { items: ActivityItem[] }) {
  if (items.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-zinc-200 bg-white/60 p-6 text-center">
        <p className="text-sm text-zinc-500">No activity in the last 48 hours.</p>
      </div>
    )
  }
  return (
    <div className="rounded-xl border border-zinc-200 bg-white shadow-sm divide-y divide-zinc-100">
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
      <div className="mt-0.5 shrink-0 text-xxs font-mono font-medium uppercase tracking-[0.12em] tabular-nums text-zinc-400 w-12">
        {relativeTime(item.at)}
      </div>
      <div className={`mt-0.5 shrink-0 inline-flex items-center rounded px-1.5 py-0.5 text-xxs font-mono font-medium tracking-[0.12em] ${tag.cls}`}>
        {tag.label}
      </div>
      <div className="min-w-0 flex-1">
        <div className="text-sm font-medium text-zinc-900 truncate">{item.label ?? '—'}</div>
        <div className="text-xs text-zinc-500 truncate">
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
    case 'paid':      return { label: 'PAID',     cls: 'bg-brand-50 text-brand-700 ring-1 ring-brand-100' }
    case 'lead-warm': return { label: 'LEAD',     cls: 'bg-amber-50 text-amber-700 ring-1 ring-amber-100' }
  }
}

function KpiMini({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <div>
      <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">{label}</div>
      <div className="mt-1 text-base font-semibold text-zinc-900 tabular-nums">{value}</div>
      {sub ? <div className="text-xs text-zinc-500 tabular-nums">{sub}</div> : null}
    </div>
  )
}

function RenewalStatusBadge({ status }: { status: string }) {
  const map: Record<string, string> = {
    Completed:  'bg-brand-50 text-brand-700 ring-1 ring-brand-100',
    Processing: 'bg-indigo-50 text-indigo-700 ring-1 ring-indigo-100',
    Pending:    'bg-amber-50 text-amber-700 ring-1 ring-amber-100',
    Failed:     'bg-red-50 text-red-700 ring-1 ring-red-100',
  }
  const cls = map[status] ?? 'bg-zinc-100 text-zinc-700 ring-1 ring-zinc-200'
  return <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-xxs font-mono font-medium tracking-[0.12em] ${cls}`}>{status.toUpperCase()}</span>
}
