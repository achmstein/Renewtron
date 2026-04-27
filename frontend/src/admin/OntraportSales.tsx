import { useEffect, useState } from 'react'
import { api } from '../api/client'

type Sale = Awaited<ReturnType<typeof api.admin.ontraportSales>>['items'][number]

function fmtDueDate(s?: string | null) {
  if (!s) return 'Unknown'
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

  const load = async () => {
    const r = await api.admin.ontraportSales()
    setData(r)
  }
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

  return (
    <>
      <header className="relative bg-white shadow-sm">
        <div className="mx-auto max-w-7xl px-4 py-4 sm:px-6 lg:px-8">
          <h1 className="text-lg/6 font-semibold text-gray-900">Ontraport Sales</h1>
        </div>
      </header>

      <main>
        <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
          {successMessage ? (
            <div className="mb-4 rounded-md bg-green-50 p-4">
              <div className="flex">
                <div className="shrink-0">
                  <svg className="h-5 w-5 text-green-400" viewBox="0 0 20 20" fill="currentColor">
                    <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clipRule="evenodd" />
                  </svg>
                </div>
                <div className="ml-3"><p className="text-sm font-medium text-green-800">{successMessage}</p></div>
              </div>
            </div>
          ) : null}
          {errorMessage ? (
            <div className="mb-4 rounded-md bg-red-50 p-4">
              <div className="flex">
                <div className="shrink-0">
                  <svg className="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
                    <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.28 7.22a.75.75 0 00-1.06 1.06L8.94 10l-1.72 1.72a.75.75 0 101.06 1.06L10 11.06l1.72 1.72a.75.75 0 101.06-1.06L11.06 10l1.72-1.72a.75.75 0 00-1.06-1.06L10 8.94 8.28 7.22z" clipRule="evenodd" />
                  </svg>
                </div>
                <div className="ml-3"><p className="text-sm font-medium text-red-800">{errorMessage}</p></div>
              </div>
            </div>
          ) : null}

          {/* Summary Cards */}
          <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-5">
            <div className="rounded-lg bg-white p-4 shadow">
              <p className="text-sm text-gray-500">Total Synced</p>
              <p className="text-2xl font-bold text-gray-900">{data?.totalCount ?? 0}</p>
            </div>
            <div className="rounded-lg bg-white p-4 shadow">
              <p className="text-sm text-gray-500">Waiting (&gt;30 days)</p>
              <p className="text-2xl font-bold text-yellow-600">{data?.waitingCount ?? 0}</p>
            </div>
            <div className="rounded-lg bg-white p-4 shadow">
              <p className="text-sm text-gray-500">Queued</p>
              <p className="text-2xl font-bold text-blue-600">{data?.queuedCount ?? 0}</p>
            </div>
            <div className="rounded-lg bg-white p-4 shadow">
              <p className="text-sm text-gray-500">Completed</p>
              <p className="text-2xl font-bold text-green-600">{data?.completedCount ?? 0}</p>
            </div>
            <div className="rounded-lg bg-white p-4 shadow">
              <p className="text-sm text-gray-500">Failed</p>
              <p className="text-2xl font-bold text-red-600">{data?.failedCount ?? 0}</p>
            </div>
          </div>

          {/* Actions */}
          <div className="mb-6 flex gap-3">
            <button onClick={sync} disabled={isSyncing} className="inline-flex items-center rounded-md bg-blue-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-500 disabled:opacity-50">
              {isSyncing ? (
                <>
                  <svg className="mr-2 h-4 w-4 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                  </svg>
                  <span>Syncing...</span>
                </>
              ) : <span>Sync Now</span>}
            </button>
            <button onClick={processEligible} className="inline-flex items-center rounded-md bg-green-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-green-500">
              Process Eligible Renewals
            </button>
          </div>

          {/* Sales Table */}
          <div className="rounded-lg bg-white shadow overflow-hidden">
            <div className="px-4 py-5 sm:px-6 border-b border-gray-200">
              <h3 className="text-base font-semibold text-gray-900">Ontraport Sales</h3>
            </div>
            <div className="overflow-x-auto">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Business Name</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">ABN</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Contact</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Renewal Due</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Days</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Term</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Amount</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Synced</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200">
                  {sales.length === 0 ? (
                    <tr>
                      <td colSpan={9} className="px-4 py-8 text-center text-sm text-gray-500">
                        No Ontraport sales synced yet. Click "Sync Now" to fetch sales.
                      </td>
                    </tr>
                  ) : sales.map((s) => <Row key={s.id} sale={s} />)}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </main>
    </>
  )
}

function Row({ sale }: { sale: Sale }) {
  const daysUntilDue = sale.renewalDueDate
    ? Math.floor((new Date(sale.renewalDueDate).getTime() - Date.now()) / 86400000)
    : null

  const dayClass = daysUntilDue == null ? '' : daysUntilDue <= 0 ? 'text-red-600 font-bold' : daysUntilDue <= 30 ? 'text-green-600 font-semibold' : 'text-gray-500'

  return (
    <tr className="hover:bg-gray-50">
      <td className="px-4 py-3 text-sm font-medium text-gray-900">{sale.businessName}</td>
      <td className="px-4 py-3 text-sm text-gray-500 font-mono">{sale.abn}</td>
      <td className="px-4 py-3 text-sm text-gray-500">
        <div>{sale.contactName}</div>
        <div className="text-xs text-gray-400">{sale.email}</div>
      </td>
      <td className="px-4 py-3 text-sm text-gray-500">{fmtDueDate(sale.renewalDueDate)}</td>
      <td className="px-4 py-3 text-sm">
        {daysUntilDue == null ? null : <span className={dayClass}>{daysUntilDue} days</span>}
      </td>
      <td className="px-4 py-3 text-sm text-gray-500">{sale.renewalYears} yr</td>
      <td className="px-4 py-3 text-sm text-gray-500">${sale.amountPaid.toLocaleString('en-AU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</td>
      <td className="px-4 py-3 text-sm">
        <SaleStatus status={sale.status} errorMessage={sale.errorMessage} />
      </td>
      <td className="px-4 py-3 text-sm text-gray-400">{fmtSyncedAt(sale.syncedAt)}</td>
    </tr>
  )
}

function SaleStatus({ status, errorMessage }: { status: string; errorMessage?: string | null }) {
  switch (status) {
    case 'Synced': return <span className="inline-flex items-center rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-800">Synced</span>
    case 'WaitingForRenewalWindow': return <span className="inline-flex items-center rounded-full bg-yellow-100 px-2.5 py-0.5 text-xs font-medium text-yellow-800">Waiting</span>
    case 'RenewalQueued': return <span className="inline-flex items-center rounded-full bg-blue-100 px-2.5 py-0.5 text-xs font-medium text-blue-800">Queued</span>
    case 'RenewalCompleted': return <span className="inline-flex items-center rounded-full bg-green-100 px-2.5 py-0.5 text-xs font-medium text-green-800">Completed</span>
    case 'RenewalFailed': return <span title={errorMessage ?? ''} className="inline-flex items-center rounded-full bg-red-100 px-2.5 py-0.5 text-xs font-medium text-red-800">Failed</span>
    case 'NotDueForRenewal': return <span className="inline-flex items-center rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-500">Not Due</span>
    default: return <span className="inline-flex items-center rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-700">{status}</span>
  }
}
