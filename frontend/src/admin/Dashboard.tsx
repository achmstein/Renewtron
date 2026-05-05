import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, type DashboardResponse } from '../api/client'
import AdminPage from './AdminPage'
import { StatusPill } from './_shared'

const fmtMoney = (n: number) =>
  n.toLocaleString('en-AU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

const fmtDateShort = (s: string) => {
  const d = new Date(s)
  return d.toLocaleDateString('en-AU', { day: '2-digit', month: 'short' })
}

const fmtTimeShort = (s: string) => {
  const d = new Date(s)
  return d.toLocaleTimeString('en-AU', { hour: '2-digit', minute: '2-digit', hour12: false })
}

export default function Dashboard() {
  const [data, setData] = useState<DashboardResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api.admin.dashboard().then(setData).catch((e) => setError(e instanceof Error ? e.message : 'Load failed.'))
  }, [])

  if (error) {
    return (
      <AdminPage title="Overview" subtitle="Live registry overview." classification="Operations">
        <div className="bureau-card p-6">
          <div className="bureau-label" style={{ color: 'var(--stamp)' }}>Loading failure</div>
          <p className="bureau-mono mt-2 text-[12px]" style={{ color: 'var(--ink)' }}>{error}</p>
        </div>
      </AdminPage>
    )
  }

  if (!data) {
    return (
      <AdminPage title="Overview" subtitle="Live registry overview." classification="Operations">
        <div className="bureau-meta animate-pulse">Loading registry…</div>
      </AdminPage>
    )
  }

  const { stats, recentSearches, recentRenewals } = data
  const successRate = stats.totalSearches > 0
    ? ((stats.successfulSearches * 100) / stats.totalSearches)
    : 0

  return (
    <AdminPage
      title="Overview"
      subtitle="Live registry of business-name renewals, ABN searches, lead pipeline and ATO onboarding."
      classification="Operations"
    >
      {/* Ledger row — primary metrics */}
      <section>
        <div className="mb-3 flex items-center justify-between">
          <div className="bureau-label">Section A · Aggregate Counters</div>
          <div className="bureau-meta hidden sm:block">Refresh on page load</div>
        </div>
        <div className="grid grid-cols-1 gap-px bg-[var(--hairline)] sm:grid-cols-2 lg:grid-cols-4 border border-[var(--hairline)]">
          <Ledger
            href="/admin/searches"
            label="Total Searches"
            value={stats.totalSearches.toLocaleString()}
            footer={
              <>
                <span style={{ color: 'var(--verdict)' }}>{successRate.toFixed(1)}%</span>
                <span style={{ color: 'var(--ledger)' }}> success rate</span>
              </>
            }
            kicker="A.01"
          />
          <Ledger
            label="Successful Searches"
            value={stats.successfulSearches.toLocaleString()}
            footer={`of ${stats.totalSearches.toLocaleString()} attempted`}
            kicker="A.02"
            verdict
          />
          <Ledger
            href="/admin/renewals"
            label="Renewals Initiated"
            value={stats.renewalsInitiated.toLocaleString()}
            footer="Operator queue"
            kicker="A.03"
          />
          <Ledger
            href="/admin/leads"
            label="Total Leads"
            value={stats.totalLeads.toLocaleString()}
            footer={
              <>
                <span style={{ color: 'var(--verdict)' }}>{stats.convertedLeads}</span>
                <span style={{ color: 'var(--ledger)' }}> converted · </span>
                <span style={{ color: 'var(--caution)' }}>{stats.notDueLeads}</span>
                <span style={{ color: 'var(--ledger)' }}> not due</span>
              </>
            }
            kicker="A.04"
          />
        </div>
      </section>

      {/* Sales comparison */}
      <section className="mt-10">
        <div className="mb-3 flex items-center justify-between">
          <div className="bureau-label">Section B · Sales Comparison · This Month</div>
          <div className="bureau-meta hidden sm:block">By origin</div>
        </div>
        <div className="grid grid-cols-1 gap-px bg-[var(--hairline)] sm:grid-cols-2 border border-[var(--hairline)]">
          <SalesPanel
            kicker="B.01"
            label="Renewtron Direct"
            count={stats.renewtronDirectCount}
            revenue={stats.renewtronDirectRevenue}
          />
          <SalesPanel
            kicker="B.02"
            label="Ontraport"
            count={stats.ontraportCount}
            revenue={stats.ontraportRevenue}
            href="/admin/ontraport-sales"
          />
        </div>
      </section>

      {/* Quick actions strip */}
      <section className="mt-10">
        <div className="mb-3 bureau-label">Section C · Operator Console</div>
        <div className="grid grid-cols-1 gap-px bg-[var(--hairline)] sm:grid-cols-2 lg:grid-cols-4 border border-[var(--hairline)]">
          <QuickAction to="/admin/searches" code="C.01" label="View Search Logs" hint="Monitor ABN searches" />
          <QuickAction to="/admin/renewals" code="C.02" label="Manage Renewals" hint="Retry failed renewals" />
          <QuickAction to="/admin/manual-renewal" code="C.03" label="Manual Renewal" hint="File a one-off case" />
          <QuickAction to="/admin/ato-onboarding" code="C.04" label="ATO Onboarding" hint="Open onboarding queue" />
        </div>
      </section>

      {/* Recent activity columns */}
      <section className="mt-10 grid grid-cols-1 gap-6 lg:grid-cols-2">
        <ActivityColumn
          kicker="D.01"
          title="Recent Searches"
          viewAll={{ to: '/admin/searches', label: 'All searches →' }}
          empty="No searches recorded."
          items={recentSearches.slice(0, 6).map((s) => ({
            id: s.id,
            primary: `ABN ${s.abn}`,
            secondary: `${fmtDateShort(s.searchedAt)} · ${fmtTimeShort(s.searchedAt)} · ${s.resultsCount} results`,
            stamp: s.success ? 'OK' : 'Failed',
          }))}
        />
        <ActivityColumn
          kicker="D.02"
          title="Recent Renewals"
          viewAll={{ to: '/admin/renewals', label: 'All renewals →' }}
          empty="No renewals recorded."
          items={recentRenewals.slice(0, 6).map((r) => ({
            id: r.id,
            primary: r.businessName ?? 'Unnamed business',
            secondary: `${fmtDateShort(r.initiatedAt)} · ${fmtTimeShort(r.initiatedAt)}`,
            stamp: r.status,
          }))}
        />
      </section>

      {/* Footer rule */}
      <div className="mt-12 flex items-center justify-between border-t-2 border-[var(--ink)] pt-4">
        <span className="bureau-meta">End of dossier</span>
        <span className="bureau-meta">Bureau of Business Names · Registry section terminated</span>
      </div>
    </AdminPage>
  )
}

/* ─────────────────────────────────────────────
   Sub-components — bureau primitives for Dashboard
   ───────────────────────────────────────────── */

function Ledger({
  label, value, footer, kicker, href, verdict,
}: {
  label: string
  value: string
  footer: React.ReactNode
  kicker: string
  href?: string
  verdict?: boolean
}) {
  const inner = (
    <div className="bureau-ledger h-full" style={{ background: 'var(--card)' }}>
      <div className="flex items-baseline justify-between">
        <div className="bureau-label">{label}</div>
        <div className="bureau-mono text-[10px]" style={{ color: 'var(--ledger-soft)' }}>{kicker}</div>
      </div>
      <div className="mt-3 flex items-baseline gap-2">
        <span className="bureau-ledger-num" style={{ color: verdict ? 'var(--verdict)' : 'var(--ink)' }}>
          {value}
        </span>
      </div>
      <div className="bureau-mono mt-3 text-[11px] tracking-wide" style={{ color: 'var(--ink-soft)' }}>
        {footer}
      </div>
    </div>
  )
  if (href) {
    return (
      <Link to={href} className="block transition-colors hover:bg-[var(--paper-deep)]">
        {inner}
      </Link>
    )
  }
  return inner
}

function SalesPanel({
  label, count, revenue, kicker, href,
}: {
  label: string
  count: number
  revenue: number
  kicker: string
  href?: string
}) {
  const inner = (
    <div className="p-6 h-full" style={{ background: 'var(--card)' }}>
      <div className="flex items-baseline justify-between">
        <div className="bureau-label">{label}</div>
        <div className="bureau-mono text-[10px]" style={{ color: 'var(--ledger-soft)' }}>{kicker}</div>
      </div>
      <div className="mt-4 flex items-end justify-between">
        <div>
          <div
            className="bureau-display"
            style={{ fontSize: 36, lineHeight: 1, fontWeight: 700, letterSpacing: '-0.02em' }}
          >
            {count.toLocaleString()}
          </div>
          <div className="bureau-meta mt-1.5">sales recorded</div>
        </div>
        <div className="text-right">
          <div
            className="bureau-mono"
            style={{ fontSize: 18, color: 'var(--ink)', fontWeight: 500, letterSpacing: '-0.01em' }}
          >
            ${fmtMoney(revenue)}
          </div>
          <div className="bureau-meta mt-1.5">gross revenue · AUD</div>
        </div>
      </div>
      {href ? (
        <div className="bureau-mono mt-5 inline-block border-b border-[var(--stamp)] pb-0.5 text-[11px]"
          style={{ color: 'var(--stamp)', letterSpacing: '0.08em', textTransform: 'uppercase' }}>
          Inspect →
        </div>
      ) : null}
    </div>
  )
  if (href) {
    return (
      <Link to={href} className="block transition-colors hover:bg-[var(--paper-deep)]">
        {inner}
      </Link>
    )
  }
  return inner
}

function QuickAction({ to, code, label, hint }: { to: string; code: string; label: string; hint: string }) {
  return (
    <Link
      to={to}
      className="group flex flex-col p-5 transition-colors hover:bg-[var(--paper-deep)]"
      style={{ background: 'var(--card)' }}
    >
      <div className="flex items-baseline justify-between">
        <div className="bureau-mono text-[10px]" style={{ color: 'var(--ledger-soft)', letterSpacing: '0.1em' }}>{code}</div>
        <div
          className="bureau-mono text-[11px] opacity-0 transition-opacity group-hover:opacity-100"
          style={{ color: 'var(--stamp)', letterSpacing: '0.08em' }}
        >
          ↗
        </div>
      </div>
      <div
        className="bureau-display mt-2 text-[18px] font-semibold leading-tight"
        style={{ color: 'var(--ink)' }}
      >
        {label}
      </div>
      <div className="bureau-mono mt-1 text-[11px]" style={{ color: 'var(--ledger)', textTransform: 'uppercase', letterSpacing: '0.06em' }}>
        {hint}
      </div>
    </Link>
  )
}

function ActivityColumn({
  kicker, title, viewAll, items, empty,
}: {
  kicker: string
  title: string
  viewAll: { to: string; label: string }
  items: Array<{ id: string; primary: string; secondary: string; stamp: string }>
  empty: string
}) {
  return (
    <div className="bureau-card">
      <div className="flex items-center justify-between border-b-2 border-[var(--ink)] px-5 py-3" style={{ background: 'var(--paper-deep)' }}>
        <div className="flex items-baseline gap-3">
          <span className="bureau-mono text-[10px]" style={{ color: 'var(--ledger-soft)', letterSpacing: '0.1em' }}>{kicker}</span>
          <h3 className="bureau-display text-[15px] font-semibold tracking-tight" style={{ color: 'var(--ink)' }}>{title}</h3>
        </div>
        <Link
          to={viewAll.to}
          className="bureau-mono border-b border-[var(--stamp)] pb-0.5 text-[10.5px]"
          style={{ color: 'var(--stamp)', letterSpacing: '0.08em', textTransform: 'uppercase' }}
        >
          {viewAll.label}
        </Link>
      </div>
      {items.length === 0 ? (
        <div className="px-5 py-8 text-center bureau-meta">{empty}</div>
      ) : (
        <ul>
          {items.map((it) => (
            <li
              key={it.id}
              className="flex items-center justify-between gap-4 border-b border-[var(--hairline)] px-5 py-3 transition-colors hover:bg-[var(--paper-deep)] last:border-b-0"
            >
              <div className="min-w-0">
                <div className="bureau-mono truncate text-[12.5px] font-medium" style={{ color: 'var(--ink)' }}>
                  {it.primary}
                </div>
                <div className="bureau-meta mt-0.5">{it.secondary}</div>
              </div>
              <StatusPill status={it.stamp} />
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
