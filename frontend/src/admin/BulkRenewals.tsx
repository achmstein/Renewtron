import { useEffect, useRef, useState } from 'react'
import { api } from '../api/client'

type Upload = Awaited<ReturnType<typeof api.admin.bulkRenewals>>['items'][number]

function fmtDueDate(s?: string | null) {
  if (!s) return 'Unknown'
  return new Date(s).toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' })
}
function fmtUploadedAt(s: string) {
  const d = new Date(s)
  const dd = String(d.getDate()).padStart(2, '0')
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const hh = String(d.getHours()).padStart(2, '0')
  const min = String(d.getMinutes()).padStart(2, '0')
  return `${dd}/${mm} ${hh}:${min}`
}

export default function BulkRenewals() {
  const [data, setData] = useState<Awaited<ReturnType<typeof api.admin.bulkRenewals>> | null>(null)
  const [isUploading, setIsUploading] = useState(false)
  const [successMessage, setSuccessMessage] = useState('')
  const [errorMessage, setErrorMessage] = useState('')
  const inputRef = useRef<HTMLInputElement>(null)

  const load = async () => { setData(await api.admin.bulkRenewals()) }
  useEffect(() => { void load() }, [])

  const upload = async (file: File) => {
    setSuccessMessage(''); setErrorMessage('')
    if (!file.name.toLowerCase().endsWith('.xlsx')) {
      setErrorMessage('Only .xlsx files are supported.')
      if (inputRef.current) inputRef.current.value = ''
      return
    }
    setIsUploading(true)
    try {
      const r = await api.admin.uploadBulkRenewals(file)
      let msg = `Processed ${r.totalRows} rows: ${r.importedCount} imported, ${r.skippedDuplicates} duplicates skipped, ${r.skippedInvalid} invalid.`
      if (r.errors.length > 0) msg += ' First errors: ' + r.errors.slice(0, 3).join('; ')
      setSuccessMessage(msg)
      await load()
    } catch (e) {
      setErrorMessage(`Upload failed: ${e instanceof Error ? e.message : 'unknown error'}`)
    } finally {
      setIsUploading(false)
      if (inputRef.current) inputRef.current.value = ''
    }
  }

  const processEligible = async () => {
    setSuccessMessage(''); setErrorMessage('')
    try {
      const r = await api.admin.processEligibleBulk()
      setSuccessMessage(r.message)
    } catch (e) {
      setErrorMessage(e instanceof Error ? e.message : 'Failed')
    }
  }

  const uploads = data?.items ?? []

  return (
    <>
      <header className="relative bg-white shadow-sm">
        <div className="mx-auto max-w-7xl px-4 py-4 sm:px-6 lg:px-8">
          <h1 className="text-lg/6 font-semibold text-gray-900">Bulk Renewals</h1>
          <p className="mt-1 text-sm text-gray-500">Upload .xlsx batches — duplicates skipped, renewals auto-fire within the 29-day window.</p>
        </div>
      </header>

      <main>
        <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
          {successMessage ? (
            <div className="mb-4 rounded-md bg-green-50 p-4">
              <p className="text-sm font-medium text-green-800">{successMessage}</p>
            </div>
          ) : null}
          {errorMessage ? (
            <div className="mb-4 rounded-md bg-red-50 p-4">
              <p className="text-sm font-medium text-red-800">{errorMessage}</p>
            </div>
          ) : null}

          {/* Summary Cards */}
          <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-5">
            <div className="rounded-lg bg-white p-4 shadow">
              <p className="text-sm text-gray-500">Total Uploaded</p>
              <p className="text-2xl font-bold text-gray-900">{data?.totalCount ?? 0}</p>
            </div>
            <div className="rounded-lg bg-white p-4 shadow">
              <p className="text-sm text-gray-500">Waiting (&gt;29 days)</p>
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

          {/* Upload panel */}
          <div className="mb-6 rounded-lg bg-white p-6 shadow">
            <h2 className="text-base font-semibold text-gray-900">Upload Excel File</h2>
            <p className="mt-1 text-sm text-gray-500">
              Expected columns: <code>Registered Business Name</code>, <code>Business Name ABN Unique</code>,{' '}
              <code>Date Business Name Renewal Due</code>, <code>Name</code>, <code>Renewal Term</code>.{' '}
              Duplicates (same ABN already in Bulk or Ontraport) are skipped. Renewals auto-fire when the due date is within 29 days.
            </p>
            <div className="mt-4 flex items-center gap-3">
              <input
                ref={inputRef}
                type="file"
                accept=".xlsx"
                onChange={(e) => e.target.files?.[0] && upload(e.target.files[0])}
                className="block w-full text-sm text-gray-700 file:mr-4 file:rounded-md file:border-0 file:bg-blue-600 file:px-4 file:py-2 file:text-sm file:font-semibold file:text-white hover:file:bg-blue-500"
              />
              {isUploading ? <span className="text-sm text-gray-500">Uploading...</span> : null}
            </div>
          </div>

          {/* Actions */}
          <div className="mb-6 flex gap-3">
            <button onClick={processEligible} className="inline-flex items-center rounded-md bg-green-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-green-500">
              Process Eligible Renewals
            </button>
          </div>

          {/* Table */}
          <div className="rounded-lg bg-white shadow overflow-hidden">
            <div className="px-4 py-5 sm:px-6 border-b border-gray-200">
              <h3 className="text-base font-semibold text-gray-900">Uploaded Renewals</h3>
            </div>
            <div className="overflow-x-auto">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Business Name</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">ABN</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Owner</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Renewal Due</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Days</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Term</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Amount</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Uploaded</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200">
                  {uploads.length === 0 ? (
                    <tr>
                      <td colSpan={9} className="px-4 py-8 text-center text-sm text-gray-500">
                        No bulk renewals uploaded yet. Drop an .xlsx file above to start.
                      </td>
                    </tr>
                  ) : uploads.map((u) => <UploadRow key={u.id} u={u} />)}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </main>
    </>
  )
}

function UploadRow({ u }: { u: Upload }) {
  const days = u.renewalDueDate
    ? Math.floor((new Date(u.renewalDueDate).getTime() - Date.now()) / 86400000)
    : null
  const dayClass = days == null ? '' : days <= 0 ? 'text-red-600 font-bold' : days <= 29 ? 'text-green-600 font-semibold' : 'text-gray-500'
  return (
    <tr className="hover:bg-gray-50">
      <td className="px-4 py-3 text-sm font-medium text-gray-900">{u.businessName}</td>
      <td className="px-4 py-3 text-sm text-gray-500 font-mono">{u.abn}</td>
      <td className="px-4 py-3 text-sm text-gray-500">{u.ownerName ?? '—'}</td>
      <td className="px-4 py-3 text-sm text-gray-500">{fmtDueDate(u.renewalDueDate)}</td>
      <td className="px-4 py-3 text-sm">{days == null ? null : <span className={dayClass}>{days} days</span>}</td>
      <td className="px-4 py-3 text-sm text-gray-500">{u.renewalYears} yr</td>
      <td className="px-4 py-3 text-sm text-gray-500">${u.amount.toLocaleString('en-AU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</td>
      <td className="px-4 py-3 text-sm"><BulkStatus status={u.status} errorMessage={u.errorMessage} /></td>
      <td className="px-4 py-3 text-sm text-gray-400">{fmtUploadedAt(u.uploadedAt)}</td>
    </tr>
  )
}

function BulkStatus({ status, errorMessage }: { status: string; errorMessage?: string | null }) {
  switch (status) {
    case 'WaitingForRenewalWindow': return <span className="inline-flex items-center rounded-full bg-yellow-100 px-2.5 py-0.5 text-xs font-medium text-yellow-800">Waiting</span>
    case 'RenewalQueued': return <span className="inline-flex items-center rounded-full bg-blue-100 px-2.5 py-0.5 text-xs font-medium text-blue-800">Queued</span>
    case 'RenewalCompleted': return <span className="inline-flex items-center rounded-full bg-green-100 px-2.5 py-0.5 text-xs font-medium text-green-800">Completed</span>
    case 'RenewalFailed': return <span title={errorMessage ?? ''} className="inline-flex items-center rounded-full bg-red-100 px-2.5 py-0.5 text-xs font-medium text-red-800">Failed</span>
    case 'NotDueForRenewal': return <span className="inline-flex items-center rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-500">Not Due</span>
    case 'Skipped': return <span className="inline-flex items-center rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-500">Skipped</span>
    default: return <span className="inline-flex items-center rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-700">{status}</span>
  }
}
