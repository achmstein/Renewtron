import { useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { api, type ActivityItem, type ActivityKind, type DashboardResponse } from '../api/client'

const fmtMoney0 = (n: number) =>
  '$' + Math.round(n).toLocaleString('en-AU')
const fmtMoney2 = (n: number) =>
  '$' + n.toLocaleString('en-AU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

function relativeTime(iso: string) {
  const t = new Date(iso).getTime()
  const diff = Date.now() - t
  const s = Math.floor(diff / 1000)
  if (s < 60) return `${s}s ago`
  const m = Math.floor(s / 60)
  if (m < 60) return `${m}m ago`
  const h = Math.floor(m / 60)
  if (h < 24) return `${h}h ago`
  const d = Math.floor(h / 24)
  return `${d}d ago`
}

export default function Dashboard() {
  const [data, setData] = useState<DashboardResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    const load = () => {
      api.admin.dashboard()
        .then((d) => { if (!cancelled) setData(d) })
        .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Load failed.') })
    }
    load()
    const t = setInterval(load, 30_000)
    return () => { cancelled = true; clearInterval(t) }
  }, [])

  if (error) {
    return (
      <div className="mx-auto max-w-7xl px-4 py-10 sm:px-6 lg:px-8">
        <div className="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">{error}</div>
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
  const conv = stats.totalLeads > 0 ? (stats.convertedLeads * 100) / stats.totalLeads : 0
  const mtdRevenue = stats.renewtronDirectRevenue + stats.ontraportRevenue
  const mtdSales = stats.renewtronDirectCount + stats.ontraportCount

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader kicker="OPS" title="Today's opportunity" subtitle="Where the revenue is hiding right now — and what to do about it." />

      <Hero stats={stats} />

      {/* Action queue — full width */}
      <div className="mt-10">
        {(() => {
          const actionItems = [
            {
              key: 'abandoned',
              tone: 'emerald' as const,
              kicker: 'WIN-BACK',
              count: stats.abandonedAtPaymentCount,
              value: fmtMoney0(stats.abandonedAtPaymentValue),
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
              key: 'stuck',
              tone: 'red' as const,
              kicker: 'STUCK',
              count: stats.stuckAtoCount,
              value: '',
              title: 'ATO stuck > 24h',
              detail: 'Renewals that completed but never finished onboarding to the ATO.',
              ctaLabel: 'Investigate →',
              to: '/admin/ato-onboarding?status=Pending',
              hideValue: true,
            },
            {
              key: 'repeat',
              tone: 'zinc' as const,
              kicker: 'REPEAT',
              count: stats.pastCustomersDueSoonCount,
              value: fmtMoney0(stats.pastCustomersDueSoonValue),
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
                        hideValue={item.hideValue}
                      />
                    ))}
                  </div>
                )}
              </div>
            </>
          )
        })()}
      </div>

      {/* Status row: ATO + MTD, 50/50 */}
      <div className="mt-10 grid grid-cols-1 lg:grid-cols-2 gap-6 items-stretch">
        <AtoStatusWidget />

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

function AtoStatusWidget() {
  const [loading, setLoading] = useState(true)
  const [errored, setErrored] = useState(false)
  const [authenticated, setAuthenticated] = useState(false)
  const [agents, setAgents] = useState<{ abn: string; name: string }[]>([])
  const [defaultAgentAbn, setDefaultAgentAbn] = useState('')
  const [defaultAgentName, setDefaultAgentName] = useState('')
  const [selectedAbn, setSelectedAbn] = useState('')
  const [saving, setSaving] = useState(false)
  const [savedJustNow, setSavedJustNow] = useState(false)

  useEffect(() => {
    let cancelled = false
    const run = async () => {
      setLoading(true)
      setErrored(false)
      try {
        const [agentsRes, settings] = await Promise.all([
          api.admin.atoAgents(),
          api.admin.settings(),
        ])
        if (cancelled) return
        setAuthenticated(agentsRes.authenticated)
        setAgents(agentsRes.agents)
        setDefaultAgentAbn(settings.atoAgent.defaultAgentAbn)
        setDefaultAgentName(settings.atoAgent.defaultAgentName)
        setSelectedAbn(settings.atoAgent.defaultAgentAbn || agentsRes.selectedAgentAbn || '')
      } catch {
        if (!cancelled) setErrored(true)
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    void run()
    return () => { cancelled = true }
  }, [])

  const onSave = async () => {
    const found = agents.find((a) => a.abn === selectedAbn)
    if (!found) return
    setSaving(true)
    try {
      await api.admin.updateAtoAgent({ defaultAgentAbn: found.abn, defaultAgentName: found.name })
      setDefaultAgentAbn(found.abn)
      setDefaultAgentName(found.name)
      setSavedJustNow(true)
      setTimeout(() => setSavedJustNow(false), 2200)
    } finally {
      setSaving(false)
    }
  }

  const Wrap = ({ tone, label, children }: { tone: 'emerald' | 'amber' | 'red' | 'zinc'; label: string; children: ReactNode }) => (
    <div className="rounded-xl border border-zinc-200 bg-white p-5 shadow-sm h-full flex flex-col">
      <div className="flex items-center justify-between gap-3">
        <SectionTitle kicker="STATUS" title="ATO integration" />
        <StatusDot tone={tone} label={label} />
      </div>
      <div className="flex-1 flex flex-col">{children}</div>
    </div>
  )

  if (loading) {
    return <Wrap tone="zinc" label="checking…"><p className="mt-3 text-sm text-zinc-500">Checking ATO session…</p></Wrap>
  }

  if (errored) {
    return (
      <Wrap tone="zinc" label="unknown">
        <p className="mt-3 text-sm text-zinc-500">Couldn't reach the ATO API. Open Settings to inspect.</p>
        <Link to="/admin/settings" className="mt-3 inline-flex items-center gap-1 text-sm font-medium text-brand-700 hover:text-brand-800">Open Settings →</Link>
      </Wrap>
    )
  }

  if (!authenticated) {
    return (
      <Wrap tone="red" label="not connected">
        <p className="mt-3 text-sm text-zinc-500">No active ATO session. New renewals won't be onboarded until you authenticate.</p>
        <Link to="/admin/settings" className="mt-3 inline-flex items-center gap-1 text-sm font-medium text-brand-700 hover:text-brand-800">Authenticate in Settings →</Link>
      </Wrap>
    )
  }

  const noDefault = !defaultAgentAbn

  return (
    <Wrap tone={noDefault ? 'amber' : 'emerald'} label={noDefault ? 'no default' : 'connected'}>
      {!noDefault ? (
        <div className="mt-3">
          <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">Default agent</div>
          <div className="mt-1 text-sm font-medium text-zinc-900 truncate" title={defaultAgentName}>{defaultAgentName || '—'}</div>
          <div className="font-mono tabular-nums text-xs text-zinc-500">ABN {defaultAgentAbn}</div>
        </div>
      ) : (
        <p className="mt-3 text-sm text-zinc-500">ATO is authenticated, but no default agent is set. Onboarding will be skipped until you pick one.</p>
      )}

      {agents.length > 0 ? (
        <div className="mt-4 space-y-2">
          <label className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">{noDefault ? 'Pick agent' : 'Change agent'}</label>
          <div className="flex gap-2">
            <select
              value={selectedAbn}
              onChange={(e) => setSelectedAbn(e.target.value)}
              className="flex-1 min-w-0 rounded-md border-zinc-300 text-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20"
            >
              <option value="">— select —</option>
              {agents.map((a) => (
                <option key={a.abn} value={a.abn}>{a.name}</option>
              ))}
            </select>
            <button
              type="button"
              onClick={onSave}
              disabled={!selectedAbn || selectedAbn === defaultAgentAbn || saving}
              className="rounded-md bg-zinc-900 text-white px-3 py-1.5 text-sm font-medium hover:bg-zinc-800 disabled:opacity-40 disabled:cursor-not-allowed transition shrink-0"
            >
              {saving ? 'Saving…' : savedJustNow ? 'Saved ✓' : 'Set'}
            </button>
          </div>
        </div>
      ) : (
        <p className="mt-3 text-xs text-zinc-500">No agents available on the active ATO session.</p>
      )}
    </Wrap>
  )
}

function StatusDot({ tone, label }: { tone: 'emerald' | 'amber' | 'red' | 'zinc'; label: string }) {
  const dot = tone === 'emerald' ? 'bg-brand-500' : tone === 'amber' ? 'bg-amber-500' : tone === 'red' ? 'bg-red-500' : 'bg-zinc-400'
  const text = tone === 'emerald' ? 'text-brand-700' : tone === 'amber' ? 'text-amber-700' : tone === 'red' ? 'text-red-700' : 'text-zinc-500'
  return (
    <div className={`inline-flex items-center gap-1.5 text-xxs font-mono uppercase tracking-[0.14em] ${text}`}>
      <span className={`h-1.5 w-1.5 rounded-full ${dot}`}></span>
      {label}
    </div>
  )
}

function CaughtUp() {
  return (
    <div className="rounded-xl border border-dashed border-brand-200 bg-brand-50/50 p-8 text-center">
      <div className="mx-auto h-10 w-10 rounded-full bg-brand-100 flex items-center justify-center">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="h-5 w-5 text-brand-600">
          <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
        </svg>
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
    stuckAtoCount: s.stuckAtoCount ?? 0,
    pastCustomersDueSoonCount: s.pastCustomersDueSoonCount ?? 0,
    pastCustomersDueSoonValue: s.pastCustomersDueSoonValue ?? 0,
  }
}

function PageHeader({ kicker, title, subtitle }: { kicker: string; title: string; subtitle?: string }) {
  return (
    <div className="mb-8">
      <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">{kicker}</div>
      <h1 className="mt-1 text-2xl font-semibold text-zinc-900 tracking-tight">{title}</h1>
      {subtitle ? <p className="mt-1 text-sm text-zinc-500">{subtitle}</p> : null}
    </div>
  )
}

function SectionTitle({ kicker, title, right }: { kicker: string; title: string; right?: ReactNode }) {
  return (
    <div className="flex items-end justify-between gap-3">
      <div>
        <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500">{kicker}</div>
        <h2 className="mt-0.5 text-base font-semibold text-zinc-900 tracking-tight">{title}</h2>
      </div>
      {right ? <div className="text-xs font-mono text-zinc-500 tabular-nums">{right}</div> : null}
    </div>
  )
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
          <HeroStat label="Abandoned" value={fmtMoney0(stats.abandonedAtPaymentValue)} count={stats.abandonedAtPaymentCount} />
          <HeroStat label="Pipeline" value={fmtMoney0(stats.ontraportPipelineValue)} count={stats.ontraportPipelineCount} />
          <HeroStat label="Failed" value={fmtMoney0(stats.failedRenewalValue)} count={stats.failedRenewalCount} />
          <HeroStat label="Repeat" value={fmtMoney0(stats.pastCustomersDueSoonValue)} count={stats.pastCustomersDueSoonCount} />
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
    case 'ato-ok':    return { label: 'ATO/OK',   cls: 'bg-indigo-50 text-indigo-700 ring-1 ring-indigo-100' }
    case 'ato-fail':  return { label: 'ATO/FAIL', cls: 'bg-red-50 text-red-700 ring-1 ring-red-100' }
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

function Th({ children }: { children: ReactNode }) {
  return (
    <th className="px-4 py-2.5 text-left text-xxs font-mono font-medium uppercase tracking-[0.12em] text-zinc-500 border-b border-zinc-200">
      {children}
    </th>
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
