import { useEffect, useState } from 'react'
import { api } from '../api/client'
import AdminPage from './AdminPage'
import { Banner, Section, StatTile, StatusPill } from './_shared'

type Sale = Awaited<ReturnType<typeof api.admin.ontraportSales>>['items'][number]

function fmtDueDate(s?: string | null) {
  if (!s) return '—'
  return new Date(s).toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' })
}
function fmtSyncedAt(s: string) {
  const d = new Date(s)
  const dd = String(d.getDate()).padStart(2, '0')
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const hh = String(d.getHours()).padStart(2, '0')
  const min = String(d.getMinutes()).padStart(2, '0')
  return `${dd}/${mm} ${hh}:${min}`
}

export default function OntraportSales() {
  const [data, setData] = useState<Awaited<ReturnType<typeof api.admin.ontraportSales>> | null>(null)
  const [isSyncing, setIsSyncing] = useState(false)
  const [successMessage, setSuccessMessage] = useState('')
  const [errorMessage, setErrorMessage] = useState('')

  const load = async () => { setData(await api.admin.ontraportSales()) }
  useEffect(() => { void load() }, [])

  const sync = async () => {
    setIsSyncing(true); setSuccessMessage(''); setErrorMessage('')
    try {
      const r = await api.admin.syncOntraport()
      setSuccessMessage(r.message)
      await load()
    } catch (e) {
      setErrorMessage(e instanceof Error ? `Sync failed: ${e.message}` : 'Sync failed')
    } finally {
      setIsSyncing(false)
    }
  }
  const processEligible = async () => {
    setSuccessMessage(''); setErrorMessage('')
    try {
      const r = await api.admin.processEligibleOntraport()
      setSuccessMessage(r.message)
    } catch (e) {
      setErrorMessage(e instanceof Error ? e.message : 'Failed')
    }
  }

  const sales = data?.items ?? []

  const actions = (
    <div className="flex gap-2">
      <button onClick={sync} disabled={isSyncing} className="bureau-btn bureau-btn-primary">
        {isSyncing ? <><span className="bureau-spin inline-block">↻</span> Syncing</> : '↻ Sync now'}
      </button>
      <button onClick={processEligible} className="bureau-btn bureau-btn-stamp">
        ✓ Process eligible
      </button>
    </div>
  )

  return (
    <AdminPage
      title="Ontraport Sales"
      subtitle="External sales synced from Ontraport — auto-fire when due date enters the renewal window."
      classification="Pipeline · External"
      actions={actions}
    >
      <div className="space-y-6">
        {successMessage ? <Banner kind="ok" message={successMessage} /> : null}
        {errorMessage ? <Banner kind="fail" message={errorMessage} /> : null}

        {/* Stat tiles */}
        <div className="grid grid-cols-1 gap-px border border-[var(--hairline)] bg-[var(--hairline)] sm:grid-cols-2 lg:grid-cols-5">
          <StatTile label="Total Synced"     value={data?.totalCount ?? 0}      kicker="A.01" />
          <StatTile label="Waiting · 30+ d" value={data?.waitingCount ?? 0}    tone="caution" kicker="A.02" />
          <StatTile label="Queued"           value={data?.queuedCount ?? 0}    tone="info"    kicker="A.03" />
          <StatTile label="Completed"        value={data?.completedCount ?? 0} tone="verdict" kicker="A.04" />
          <StatTile label="Failed"           value={data?.failedCount ?? 0}    tone="stamp"   kicker="A.05" />
        </div>

        {/* Table */}
        <Section kicker="B" title="Synced Sales" dense>
          <div className="overflow-x-auto">
            <table className="bureau-table">
              <thead>
                <tr>
                  <th>Business</th>
                  <th>ABN</th>
                  <th>Contact</th>
                  <th>Renewal Due</th>
                  <th>Days</th>
                  <th>Term</th>
                  <th>Amount</th>
                  <th>Status</th>
                  <th>Synced</th>
                </tr>
              </thead>
              <tbody>
                {sales.length === 0 ? (
                  <tr>
                    <td colSpan={9} className="bureau-meta py-12 text-center">
                      No Ontraport sales synced yet — click "Sync now" to fetch.
                    </td>
                  </tr>
                ) : sales.map((s) => <Row key={s.id} sale={s} />)}
              </tbody>
            </table>
          </div>
        </Section>
      </div>
    </AdminPage>
  )
}

function Row({ sale }: { sale: Sale }) {
  const daysUntilDue = sale.renewalDueDate
    ? Math.floor((new Date(sale.renewalDueDate).getTime() - Date.now()) / 86400000)
    : null

  const dayColor =
    daysUntilDue == null ? 'var(--ledger)'
    : daysUntilDue <= 0 ? 'var(--stamp)'
    : daysUntilDue <= 30 ? 'var(--verdict)'
    : 'var(--ledger)'

  return (
    <tr>
      <td className="text-[13px] font-medium" style={{ color: 'var(--ink)' }}>{sale.businessName}</td>
      <td className="bureau-mono whitespace-nowrap text-[12px]" style={{ color: 'var(--ink)' }}>{sale.abn}</td>
      <td>
        <div style={{ color: 'var(--ink)' }}>{sale.contactName}</div>
        <div className="bureau-mono text-[11px]" style={{ color: 'var(--ledger)' }}>{sale.email}</div>
      </td>
      <td className="whitespace-nowrap" style={{ color: 'var(--ink-soft)' }}>{fmtDueDate(sale.renewalDueDate)}</td>
      <td className="bureau-mono whitespace-nowrap text-[12px]" style={{ color: dayColor, fontWeight: 600 }}>
        {daysUntilDue == null ? '—' : `${daysUntilDue}d`}
      </td>
      <td className="bureau-mono whitespace-nowrap" style={{ color: 'var(--ink-soft)' }}>{sale.renewalYears}yr</td>
      <td className="bureau-mono whitespace-nowrap" style={{ color: 'var(--ink)' }}>${sale.amountPaid.toLocaleString('en-AU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</td>
      <td className="whitespace-nowrap" title={sale.errorMessage ?? ''}><StatusPill status={sale.status} /></td>
      <td className="bureau-mono whitespace-nowrap text-[11px]" style={{ color: 'var(--ledger)' }}>{fmtSyncedAt(sale.syncedAt)}</td>
    </tr>
  )
}
