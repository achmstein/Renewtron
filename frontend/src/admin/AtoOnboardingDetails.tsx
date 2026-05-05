import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'

type Detail = Awaited<ReturnType<typeof api.admin.atoOnboardingDetail>>

function fmtDateTime(s: string | null | undefined) {
  if (!s) return '—'
  const d = new Date(s)
  return `${d.toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })} ${d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })}`
}

function tryPretty(json: string | null | undefined) {
  if (!json) return ''
  try { return JSON.stringify(JSON.parse(json), null, 2) } catch { return json }
}

export default function AtoOnboardingDetails() {
  const { id } = useParams<{ id: string }>()
  const [detail, setDetail] = useState<Detail | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!id) return
    api.admin.atoOnboardingDetail(id).then(setDetail).catch((e) => setError(e instanceof Error ? e.message : 'Load failed'))
  }, [id])

  if (error) return <div className="mx-auto max-w-3xl p-6"><div className="rounded-md bg-red-50 text-red-800 p-4 text-sm">{error}</div></div>
  if (!detail) return <div className="mx-auto max-w-3xl p-6 text-sm text-gray-500">Loading…</div>

  return (
    <div className="mx-auto max-w-5xl px-4 sm:px-6 lg:px-8 py-6 space-y-4">
      <div>
        <Link to="/admin/ato-onboarding" className="text-sm text-indigo-600 hover:underline">← Back to ATO onboarding</Link>
      </div>

      <div className="rounded-md border border-gray-200 bg-white p-4">
        <h1 className="text-lg font-semibold">{detail.businessName}</h1>
        <div className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
          <div className="text-gray-500">ABN</div>
          <div className="font-mono">{detail.abn}</div>
          <div className="text-gray-500">Customer</div>
          <div>{detail.fullName ?? '—'} <span className="text-gray-500 text-xs">{detail.email}</span></div>
          <div className="text-gray-500">DOB</div>
          <div>{detail.dateOfBirth ?? '—'}</div>
          <div className="text-gray-500">TFN</div>
          <div className="font-mono">{detail.tfn ?? '—'}</div>
          <div className="text-gray-500">Renewal status</div>
          <div>{detail.renewalStatus}</div>
          <div className="text-gray-500">ATO status</div>
          <div>{detail.atoStatus ?? '—'}</div>
          <div className="text-gray-500">ATO job ID</div>
          <div className="font-mono text-xs">{detail.atoJobId}</div>
          <div className="text-gray-500">ATO completed</div>
          <div>{fmtDateTime(detail.atoCompletedAt)}</div>
        </div>
      </div>

      <div className="rounded-md border border-gray-200 bg-white p-4">
        <h2 className="text-sm font-medium text-gray-700 mb-2">ATO Result Data</h2>
        {detail.atoResultJson ? (
          <pre className="bg-gray-900 text-gray-100 rounded p-3 text-xs overflow-x-auto">{tryPretty(detail.atoResultJson)}</pre>
        ) : (
          <div className="text-sm text-gray-500">No result yet.</div>
        )}
      </div>
    </div>
  )
}
