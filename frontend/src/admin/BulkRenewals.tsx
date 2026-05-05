import { useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import AdminPage from './AdminPage'
import { Banner, Section, StatTile, StatusPill } from './_shared'

type Upload = Awaited<ReturnType<typeof api.admin.bulkRenewals>>['items'][number]

function fmtDueDate(s?: string | null) {
  if (!s) return '—'
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

  const actions = (
    <button onClick={processEligible} className="bureau-btn bureau-btn-stamp">✓ Process eligible</button>
  )

  return (
    <AdminPage
      title="Bulk Renewals"
      subtitle="Upload .xlsx batches — duplicates skipped, renewals auto-fire within the 29-day window."
      classification="Pipeline · Bulk"
      actions={actions}
    >
      <div className="space-y-6">
        {successMessage ? <Banner kind="ok" message={successMessage} /> : null}
        {errorMessage ? <Banner kind="fail" message={errorMessage} /> : null}

        {/* Stat tiles */}
        <div className="grid grid-cols-1 gap-px border border-[var(--hairline)] bg-[var(--hairline)] sm:grid-cols-2 lg:grid-cols-5">
          <StatTile label="Total Uploaded"   value={data?.totalCount ?? 0}     kicker="A.01" />
          <StatTile label="Waiting · 29+ d" value={data?.waitingCount ?? 0}   tone="caution" kicker="A.02" />
          <StatTile label="Queued"           value={data?.queuedCount ?? 0}    tone="info"    kicker="A.03" />
          <StatTile label="Completed"        value={data?.completedCount ?? 0} tone="verdict" kicker="A.04" />
          <StatTile label="Failed"           value={data?.failedCount ?? 0}    tone="stamp"   kicker="A.05" />
        </div>

        {/* Upload */}
        <Section kicker="B" title="Upload Excel File">
          <p className="bureau-meta mb-4" style={{ textTransform: 'none', letterSpacing: '0.02em', fontSize: 12 }}>
            Expected columns:
            {' '}<code className="bureau-mono" style={{ color: 'var(--ink)' }}>Registered Business Name</code>,
            {' '}<code className="bureau-mono" style={{ color: 'var(--ink)' }}>Business Name ABN Unique</code>,
            {' '}<code className="bureau-mono" style={{ color: 'var(--ink)' }}>Date Business Name Renewal Due</code>,
            {' '}<code className="bureau-mono" style={{ color: 'var(--ink)' }}>Name</code>,
            {' '}<code className="bureau-mono" style={{ color: 'var(--ink)' }}>Renewal Term</code>.
          </p>
          <div className="flex items-center gap-3">
            <input
              ref={inputRef}
              type="file"
              accept=".xlsx"
              onChange={(e) => e.target.files?.[0] && upload(e.target.files[0])}
              className="bureau-mono block w-full text-[12px]"
              style={{
                color: 'var(--ledger)',
              }}
            />
            {isUploading ? <span className="bureau-meta whitespace-nowrap"><span className="bureau-spin inline-block">↻</span> Uploading</span> : null}
          </div>
        </Section>

        {/* Table */}
        <Section kicker="C" title="Uploaded Renewals" dense>
          <div className="overflow-x-auto">
            <table className="bureau-table">
              <thead>
                <tr>
                  <th>Business</th>
                  <th>ABN</th>
                  <th>Owner</th>
                  <th>Renewal Due</th>
                  <th>Days</th>
                  <th>Term</th>
                  <th>Amount</th>
                  <th>Status</th>
                  <th>Uploaded</th>
                </tr>
              </thead>
              <tbody>
                {uploads.length === 0 ? (
                  <tr>
                    <td colSpan={9} className="bureau-meta py-12 text-center">
                      No bulk uploads yet — drop an .xlsx file above to start.
                    </td>
                  </tr>
                ) : uploads.map((u) => <UploadRow key={u.id} u={u} />)}
              </tbody>
            </table>
          </div>
        </Section>
      </div>
    </AdminPage>
  )
}

function UploadRow({ u }: { u: Upload }) {
  const days = u.renewalDueDate
    ? Math.floor((new Date(u.renewalDueDate).getTime() - Date.now()) / 86400000)
    : null
  const dayColor =
    days == null ? 'var(--ledger)'
    : days <= 0 ? 'var(--stamp)'
    : days <= 29 ? 'var(--verdict)'
    : 'var(--ledger)'
  return (
    <tr>
      <td className="text-[13px] font-medium" style={{ color: 'var(--ink)' }}>{u.businessName}</td>
      <td className="bureau-mono whitespace-nowrap text-[12px]" style={{ color: 'var(--ink)' }}>{u.abn}</td>
      <td style={{ color: 'var(--ink-soft)' }}>{u.ownerName ?? '—'}</td>
      <td className="whitespace-nowrap" style={{ color: 'var(--ink-soft)' }}>{fmtDueDate(u.renewalDueDate)}</td>
      <td className="bureau-mono whitespace-nowrap text-[12px]" style={{ color: dayColor, fontWeight: 600 }}>
        {days == null ? '—' : `${days}d`}
      </td>
      <td className="bureau-mono whitespace-nowrap" style={{ color: 'var(--ink-soft)' }}>{u.renewalYears}yr</td>
      <td className="bureau-mono whitespace-nowrap" style={{ color: 'var(--ink)' }}>${u.amount.toLocaleString('en-AU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</td>
      <td className="whitespace-nowrap" title={u.errorMessage ?? ''}><StatusPill status={u.status} /></td>
      <td className="bureau-mono whitespace-nowrap text-[11px]" style={{ color: 'var(--ledger)' }}>{fmtUploadedAt(u.uploadedAt)}</td>
    </tr>
  )
}
