import { useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import AdminPage from './AdminPage'
import { Banner, DefList, DefRow, Section, StatusPill } from './_shared'

type Detail = Awaited<ReturnType<typeof api.admin.renewal>>

function fmtDateTime(s: string) {
  const d = new Date(s)
  const date = d.toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })
  const time = d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })
  return `${date} ${time}`
}
function fmtDob(s: string) {
  const d = new Date(s)
  const dd = String(d.getDate()).padStart(2, '0')
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  return `${dd}/${mm}/${d.getFullYear()}`
}

export default function RenewalDetails() {
  const { id } = useParams()
  const [data, setData] = useState<Detail | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)
  const [isRetrying, setIsRetrying] = useState(false)
  const [successMessage, setSuccessMessage] = useState('')
  const [errorMessage, setErrorMessage] = useState('')
  const pollRef = useRef<number | undefined>(undefined)

  const load = async () => {
    if (!id) return
    setIsLoading(true)
    try {
      const r = await api.admin.renewal(id)
      setData(r)
    } catch {
      setNotFound(true)
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => { void load() /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [id])

  useEffect(() => {
    if (!data) return
    if (pollRef.current) { window.clearInterval(pollRef.current); pollRef.current = undefined }
    if (data.status === 'Processing') {
      pollRef.current = window.setInterval(() => { void load() }, 3000)
    }
    return () => {
      if (pollRef.current) { window.clearInterval(pollRef.current); pollRef.current = undefined }
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data?.status])

  const retry = async () => {
    if (!id) return
    setIsRetrying(true); setSuccessMessage(''); setErrorMessage('')
    try {
      const r = await api.admin.retryRenewal(id)
      setSuccessMessage(r.message)
      await load()
    } catch (e) {
      setErrorMessage(e instanceof Error ? e.message : 'Unexpected error')
    } finally {
      setIsRetrying(false)
    }
  }

  const back = <Link to="/admin/renewals" className="bureau-btn">← Back to Renewals</Link>

  if (isLoading) {
    return (
      <AdminPage title="Renewal Dossier" classification="Renewals" actions={back}>
        <div className="bureau-meta animate-pulse">Loading renewal…</div>
      </AdminPage>
    )
  }

  if (notFound || !data) {
    return (
      <AdminPage title="Renewal Not Found" classification="Renewals" actions={back}>
        <div className="bureau-card p-6">
          <p className="text-[14px]" style={{ color: 'var(--ink)' }}>The renewal request you're looking for doesn't exist.</p>
        </div>
      </AdminPage>
    )
  }

  const canRetry = data.status === 'Failed' && ((data.paymentType === 'Stripe' && data.stripePayment?.paymentStatus === 'succeeded') || data.paymentType === 'External')

  return (
    <AdminPage
      title={data.businessName}
      subtitle={`ABN ${data.abn}`}
      caseId={`RNW-${data.id.slice(0, 8).toUpperCase()}`}
      classification="Renewals · Dossier"
      actions={back}
    >
      <div className="space-y-6">
        {successMessage ? <Banner kind="ok" message={successMessage} /> : null}
        {errorMessage ? <Banner kind="fail" message={errorMessage} /> : null}

        {/* Status strip */}
        <div className="bureau-card flex flex-wrap items-center justify-between gap-4 px-5 py-4">
          <div>
            <div className="bureau-label" style={{ marginBottom: 3 }}>Verdict</div>
            <StatusPill status={data.status} />
          </div>
          <div className="text-right">
            <div className="bureau-label" style={{ marginBottom: 3 }}>Amount</div>
            <div className="bureau-display text-[22px] font-semibold" style={{ color: 'var(--ink)' }}>${data.amount.toFixed(2)}</div>
          </div>
        </div>

        <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <Section kicker="A" title="Renewal Information">
            <DefList>
              <DefRow label="Request ID" mono full>{data.id}</DefRow>
              <DefRow label="Initiated">{fmtDateTime(data.initiatedAt)}</DefRow>
              <DefRow label="Initiated By">
                <span style={{ color: data.initiatedByLabel === 'Admin' ? 'var(--stamp)' : 'var(--ink)' }}>{data.initiatedByLabel}</span>
              </DefRow>
              {data.completedAt ? <DefRow label="Completed">{fmtDateTime(data.completedAt)}</DefRow> : null}
              <DefRow label="Period">{data.renewalYears} year{data.renewalYears > 1 ? 's' : ''}</DefRow>
              <DefRow label="Customer Email">{data.email ?? '—'}</DefRow>
              <DefRow label="Mobile">{data.mobileNumber ?? '—'}</DefRow>
              <DefRow label="Date of Birth">{data.dateOfBirth ? fmtDob(data.dateOfBirth) : '—'}</DefRow>
              <DefRow label="IP Address" mono>{data.ipAddress ?? '—'}</DefRow>
            </DefList>
          </Section>

          <Section kicker="B" title="Payment Information">
            <DefList>
              <DefRow label="Payment Type">
                <StatusPill status={data.paymentType === 'External' ? 'Synced' : 'Processing'} />
              </DefRow>
              <DefRow label="Amount">
                <span className="bureau-mono text-[14px] font-medium" style={{ color: 'var(--ink)' }}>${data.amount.toFixed(2)}</span>
              </DefRow>
              {data.stripePayment ? (
                <>
                  <DefRow label="Stripe Intent" mono full>{data.stripePayment.paymentIntentId}</DefRow>
                  <DefRow label="Stripe Status">
                    <StatusPill status={data.stripePayment.paymentStatus === 'succeeded' ? 'Completed' : 'Failed'} />
                  </DefRow>
                  {data.stripePayment.paidAt ? (
                    <DefRow label="Paid At">{fmtDateTime(data.stripePayment.paidAt)}</DefRow>
                  ) : null}
                </>
              ) : null}
            </DefList>
          </Section>

          <Section kicker="C" title="ASIC Transaction">
            <DefList>
              <DefRow label="Transaction Reference" mono full>{data.transactionReference ?? '—'}</DefRow>
              <DefRow label="Tokenization ID" mono full>{data.hostedTokenizationId ?? '—'}</DefRow>
              {data.errorMessage ? (
                <DefRow label="Error Message" full>
                  <span style={{ color: 'var(--stamp)' }}>{data.errorMessage}</span>
                </DefRow>
              ) : null}
              {data.failedAtStep ? (
                <DefRow label="Failed At Step">
                  <span className="bureau-stamp bureau-stamp-fail">{data.failedAtStep}</span>
                </DefRow>
              ) : null}
            </DefList>
          </Section>

          {data.stripePayment?.cardLast4 ? (
            <Section kicker="D" title="Customer Card on File">
              <DefList>
                <DefRow label="Cardholder">{data.stripePayment.cardholderName ?? '—'}</DefRow>
                <DefRow label="Card Number" mono>**** **** **** {data.stripePayment.cardLast4}</DefRow>
                <DefRow label="Brand">{data.stripePayment.cardBrand ?? '—'}</DefRow>
                <DefRow label="Expiry">{data.stripePayment.cardExpMonth}/{data.stripePayment.cardExpYear}</DefRow>
              </DefList>
            </Section>
          ) : null}
        </div>

        {canRetry ? (
          <div className="flex justify-end border-t-2 border-[var(--ink)] pt-5">
            <button onClick={retry} disabled={isRetrying} className="bureau-btn bureau-btn-primary">
              {isRetrying ? <><span className="bureau-spin inline-block">↻</span> Retrying</> : '↻ Retry Renewal'}
            </button>
          </div>
        ) : null}
      </div>
    </AdminPage>
  )
}
