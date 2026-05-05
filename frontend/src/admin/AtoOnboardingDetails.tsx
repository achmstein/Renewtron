import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import AdminPage from './AdminPage'
import { Banner, DefList, DefRow, Section, StatusPill } from './_shared'

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

  const back = <Link to="/admin/ato-onboarding" className="bureau-btn">← Back to ATO Onboarding</Link>

  if (error) {
    return (
      <AdminPage title="ATO Onboarding · Dossier" classification="Onboarding" actions={back}>
        <Banner kind="fail" message={error} />
      </AdminPage>
    )
  }

  if (!detail) {
    return (
      <AdminPage title="ATO Onboarding · Dossier" classification="Onboarding" actions={back}>
        <div className="bureau-meta animate-pulse">Loading dossier…</div>
      </AdminPage>
    )
  }

  return (
    <AdminPage
      title={detail.businessName ?? 'Unnamed Business'}
      subtitle={`ABN ${detail.abn}`}
      caseId={`ATO-${(detail.atoJobId ?? '').slice(0, 8).toUpperCase() || 'PENDING'}`}
      classification="Onboarding · Dossier"
      actions={back}
    >
      <div className="space-y-6">
        <Section kicker="A" title="Onboarding Record">
          <DefList>
            <DefRow label="ABN" mono>{detail.abn}</DefRow>
            <DefRow label="Customer">
              <span style={{ color: 'var(--ink)' }}>{detail.fullName ?? '—'}</span>
              <span className="bureau-mono ml-2 text-[11px]" style={{ color: 'var(--ledger)' }}>{detail.email}</span>
            </DefRow>
            <DefRow label="Date of Birth">{detail.dateOfBirth ?? '—'}</DefRow>
            <DefRow label="TFN" mono>{detail.tfn ?? '—'}</DefRow>
            <DefRow label="Renewal Status"><StatusPill status={detail.renewalStatus} /></DefRow>
            <DefRow label="ATO Status">
              {detail.atoStatus ? <StatusPill status={detail.atoStatus} /> : <span style={{ color: 'var(--ledger-soft)' }}>—</span>}
            </DefRow>
            <DefRow label="ATO Job ID" mono>{detail.atoJobId ?? '—'}</DefRow>
            <DefRow label="ATO Completed">{fmtDateTime(detail.atoCompletedAt)}</DefRow>
          </DefList>
        </Section>

        <Section kicker="B" title="ATO Result Data">
          {detail.atoResultJson ? (
            <pre
              className="bureau-mono overflow-x-auto p-4 text-[11.5px] leading-relaxed"
              style={{
                background: 'var(--ink)',
                color: 'var(--paper)',
                border: '1px solid var(--ink)',
              }}
            >
              {tryPretty(detail.atoResultJson)}
            </pre>
          ) : (
            <div className="bureau-meta">No result yet.</div>
          )}
        </Section>
      </div>
    </AdminPage>
  )
}
