import { type ReactNode } from 'react'
import { useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Loader2, RefreshCw, Send, Undo2 } from 'lucide-react'
import { sileo } from 'sileo'
import { registrationsApi, type AdminRegistration, type AdminRegistrationDetail } from '../api/registrations'
import { statusOf, type Meaning } from './_status'
import { When } from './_rows'
import { DetailRow, NextStep, RecordCrumbs, RecordHeader, StoryStrip, TechnicalDetail, type StoryStep } from './_record'
import { fmtDateTime, fmtMoney2, formatAbn } from './_utils'
import { Skeletonizer } from '@/components/skeletonizer'
import { sample } from '@/components/skeleton-samples'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { pageClass } from './_ui'

/*
 * One new business name application (/admin/registrations/:id): what the customer asked for,
 * how far it got in ASIC Connect, and what a person does next (pay ASIC by BPAY, refund, retry).
 */

const SAMPLE: AdminRegistrationDetail = {
  registration: {
    id: sample.id(0), createdAt: sample.daysAgo(1), status: 'AwaitingAsicPayment', businessName: sample.business(3), abn: sample.abn(3),
    years: 1, firstName: 'Jane', lastName: 'Citizen', email: sample.email(3), phone: sample.mobile(),
    serviceAddress: '1 Martin Pl, Sydney NSW 2000', principalAddress: '50 Bridge St, Sydney NSW 2000', amount: 147,
    paidAt: sample.daysAgo(1), stripePaymentIntentId: 'pi_000000', attemptCount: 1, startedAt: sample.daysAgo(1), finishedAt: sample.daysAgo(1),
    asicTransactionReference: '1-11AQR319', abrEntityName: 'CITIZEN PTY LTD', abrEntityType: 'Incorporated Body', nameVerdict: 'Available',
    nameVerdictText: 'CITIZEN PLUMBING is currently available.', asicPayBy: 'Bpay', asicFee: 47, lodgedAt: sample.daysAgo(1),
    payBy: sample.daysAhead(12), bpayBillerCode: '17301', bpayReference: '8597040845155', airwallexTransferId: null,
    airwallexTransferStatus: null, bpayPaymentError: null, asicPaidAt: null, asicTransactionStatus: 'Validated', asicPaymentStatus: '',
    asicCheckedAt: sample.daysAgo(0), failedAtStep: null, error: null, isTest: false,
  },
  pageText: null,
  asicFeeNow: 47,
}

export default function RegistrationDetails() {
  const { id } = useParams()
  const qc = useQueryClient()
  const { data: loaded, isPending, isError } = useQuery({
    queryKey: ['admin-registration', id],
    queryFn: () => registrationsApi.admin.get(id!),
    enabled: !!id,
    refetchInterval: (q) => (q.state.data && ['Queued', 'Running'].includes(q.state.data.registration.status) ? 4000 : false),
  })
  const d = loaded ?? (isPending ? SAMPLE : undefined)
  const refresh = () => {
    void qc.invalidateQueries({ queryKey: ['admin-registration', id] })
    void qc.invalidateQueries({ queryKey: ['admin-registrations'] })
  }
  const retry = useMutation({ mutationFn: () => registrationsApi.admin.retry(id!), onSettled: refresh })
  const payAsic = useMutation({ mutationFn: (amount: number) => registrationsApi.admin.payAsic(id!, amount), onSettled: refresh })
  const refunded = useMutation({ mutationFn: () => registrationsApi.admin.refunded(id!), onSettled: refresh })

  if (isError || !d) return <div className={pageClass('py-12 text-center')}><h3 className="text-heading">Application not found</h3></div>
  const r = d.registration

  const toast = <T,>(p: Promise<T>, loading: string, done: string, failed: string) =>
    void sileo.promise(p, {
      loading: { title: loading },
      success: () => ({ title: done }),
      error: (e) => ({ title: failed, description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})

  const retryButton = (
    <Button size="sm" onClick={() => toast(retry.mutateAsync(), 'Queuing…', 'Queued for ASIC.', 'Couldn’t queue it')} disabled={retry.isPending}>
      {retry.isPending ? <Loader2 className="animate-spin" /> : <RefreshCw />}Try again
    </Button>
  )
  const refundButton = (
    <Button size="sm" variant="outline" onClick={() => toast(refunded.mutateAsync(), 'Saving…', 'Marked refunded.', 'Couldn’t mark it')} disabled={refunded.isPending}>
      <Undo2 />Mark refunded
    </Button>
  )
  const fee = d.asicFeeNow
  const bpayInFlight = r.airwallexTransferStatus && !['FAILED', 'CANCELLED', 'APPROVAL_REJECTED', 'APPROVAL_RECALLED', 'APPROVAL_BLOCKED', 'REJECTED', 'UNKNOWN'].includes(r.airwallexTransferStatus)
  const payButton = fee != null && r.bpayReference && !r.asicPaidAt && !bpayInFlight && !r.isTest ? (
    <Button size="sm" onClick={() => {
      if (!window.confirm(`Pay ASIC ${fmtMoney2(fee)} by BPAY (biller ${r.bpayBillerCode}, ref ${r.bpayReference}) through Airwallex?`)) return
      toast(payAsic.mutateAsync(fee), 'Sending BPAY…', 'BPAY payment sent through Airwallex.', 'Couldn’t pay it')
    }} disabled={payAsic.isPending}>
      {payAsic.isPending ? <Loader2 className="animate-spin" /> : <Send />}Pay ASIC {fmtMoney2(fee)} by BPAY
    </Button>
  ) : null

  let next: ReactNode
  switch (r.status) {
    case 'AwaitingPayment':
      next = <NextStep meaning="quiet" title="The customer hasn’t paid.">Started <When at={r.createdAt} />. Nothing goes to ASIC until they pay.</NextStep>
      break
    case 'Queued':
    case 'Running':
      next = <NextStep meaning="working" title="Nothing to do; it’s going through ASIC Connect.">Started <When at={r.startedAt ?? r.paidAt ?? r.createdAt} />. This page updates by itself.</NextStep>
      break
    case 'AwaitingAsicPayment':
      next = bpayInFlight
        ? <NextStep meaning="waiting" title="BPAY payment on its way.">Airwallex says {r.airwallexTransferStatus}. ASIC shows it paid once BPAY clears (a business day or two).</NextStep>
        : <NextStep meaning="action" title={`Pay ASIC’s fee by BPAY${r.payBy ? ' before ' + new Date(r.payBy).toLocaleDateString('en-AU') : ''}.`} actions={payButton}>
            Lodged <When at={r.lodgedAt} /> as {r.asicTransactionReference}. ASIC holds the name for 10 business days, then drops the application if unpaid.
            {r.bpayPaymentError ? <span className="mt-1 block text-destructive">Last try: {r.bpayPaymentError}</span> : null}
          </NextStep>
      break
    case 'Lodged':
      next = <NextStep meaning="done" title="Nothing to do.">Lodged <When at={r.lodgedAt} />{r.asicPaidAt ? <>, fee paid <When at={r.asicPaidAt} /></> : null}. ASIC emails the customer the record of registration and ASIC key.</NextStep>
      break
    case 'Refused':
      next = <NextStep meaning="action" title="Refund the customer in Stripe." actions={<>{refundButton}{retryButton}</>}>
        <span className="whitespace-pre-wrap">{r.error ?? 'ASIC wouldn’t take it.'}</span> Nothing was lodged; we emailed the customer.
      </NextStep>
      break
    case 'Refunded':
      next = <NextStep meaning="quiet" title="Refunded.">Nothing else to do.</NextStep>
      break
    default:
      next = <NextStep meaning="action" title={r.lodgedAt ? 'Lodged, but something needs a person.' : 'It didn’t get through.'}
                       actions={<>{r.lodgedAt ? null : retryButton}{refundButton}</>}>
        <span className="whitespace-pre-wrap">{r.error ?? 'No note was left.'}</span>
        {r.lodgedAt ? <span className="mt-1 block">Check {r.asicTransactionReference ?? 'the transaction'} in ASIC Connect → myTransactions before doing anything else.</span> : null}
      </NextStep>
  }

  const steps: StoryStep[] = [
    { label: 'Applied', meaning: 'done', at: r.createdAt },
    { label: 'Paid us', meaning: r.paidAt ? 'done' : 'quiet', at: r.paidAt, note: r.paidAt ? fmtMoney2(r.amount) : 'not yet' },
    { label: 'Name check', ...verdict(r) },
    { label: 'Lodged', meaning: r.lodgedAt ? 'done' : ['Refused', 'NeedsLook', 'Failed'].includes(r.status) ? 'action' : r.paidAt ? 'working' : 'quiet', at: r.lodgedAt, note: r.asicTransactionReference ?? undefined },
    { label: 'ASIC fee', meaning: r.asicPaidAt ? 'done' : r.lodgedAt ? (bpayInFlight ? 'waiting' : 'action') : 'quiet', at: r.asicPaidAt, note: r.asicPaidAt ? 'paid' : r.lodgedAt ? 'to pay' : undefined },
  ]

  return (
    <Skeletonizer loading={isPending}>
    <div className={pageClass()}>
      <RecordCrumbs client={null} businessName={r.businessName} list={{ to: '/admin/registrations', label: 'New business names' }} here="Application" />
      <RecordHeader
        title={r.businessName}
        sub={<>ABN {formatAbn(r.abn)} · {r.years} year{r.years === 1 ? '' : 's'} · applied <When at={r.createdAt} />{r.isTest ? ' · test customer (never lodged)' : ''}</>}
        status={statusOf('registration', r.status)}
      >
        {next}
        <StoryStrip steps={steps} />
      </RecordHeader>

      <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-2">
        <Card className="gap-4">
          <CardHeader><CardTitle>Application</CardTitle></CardHeader>
          <CardContent>
            <dl className="space-y-3">
              <DetailRow label="Customer" value={`${r.firstName} ${r.lastName}`} />
              <DetailRow label="Email" value={r.email} />
              <DetailRow label="Phone" value={r.phone} />
              <DetailRow label="Address for service" value={r.serviceAddress} />
              <DetailRow label="Principal place" value={r.principalAddress} />
              <DetailRow label="Paid" value={r.paidAt ? `${fmtMoney2(r.amount)} on ${fmtDateTime(r.paidAt)}` : 'not yet'} />
            </dl>
          </CardContent>
        </Card>
        <Card className="gap-4">
          <CardHeader><CardTitle>At ASIC</CardTitle></CardHeader>
          <CardContent>
            <dl className="space-y-3">
              <DetailRow label="Holder (from the ABR)" value={r.abrEntityName ? `${r.abrEntityName}${r.abrEntityType ? ` · ${r.abrEntityType}` : ''}` : '—'} />
              <DetailRow label="Name check" value={r.nameVerdictText ?? r.nameVerdict ?? '—'} />
              <DetailRow label="Transaction" value={r.asicTransactionReference ?? '—'} mono />
              <DetailRow label="BPAY" value={r.bpayReference ? `Biller ${r.bpayBillerCode} · ref ${r.bpayReference}` : '—'} mono />
              <DetailRow label="Airwallex" value={r.airwallexTransferStatus ?? '—'} />
              <DetailRow label="myTransactions" value={r.asicCheckedAt
                ? <>{r.asicTransactionStatus || '—'} · payment {r.asicPaymentStatus || '—'} (checked <When at={r.asicCheckedAt} />)</>
                : '—'} />
            </dl>
          </CardContent>
        </Card>
      </div>

      <TechnicalDetail>
        <dl className="grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-2">
          <DetailRow label="Application ID" value={<span className="break-all">{r.id}</span>} mono />
          <DetailRow label="Status" value={r.status} />
          <DetailRow label="Stripe payment" value={r.stripePaymentIntentId ?? '—'} mono />
          <DetailRow label="Attempts" value={String(r.attemptCount)} />
          <DetailRow label="Started" value={r.startedAt ? fmtDateTime(r.startedAt) : '—'} />
          <DetailRow label="Finished" value={r.finishedAt ? fmtDateTime(r.finishedAt) : '—'} />
          <DetailRow label="Stopped at" value={r.failedAtStep ?? '—'} />
          <DetailRow label="Airwallex transfer" value={r.airwallexTransferId ?? '—'} mono />
        </dl>
        {r.error ? <pre className="mt-4 max-h-48 overflow-y-auto whitespace-pre-wrap break-words rounded-md border bg-muted/50 p-3 text-caption">{r.error}</pre> : null}
        {d.pageText ? (
          <>
            <div className="mt-4 text-caption text-muted-foreground">Last ASIC page we saw</div>
            <pre className="mt-1 max-h-72 overflow-y-auto whitespace-pre-wrap break-words rounded-md border bg-muted/50 p-3 text-caption">{d.pageText}</pre>
          </>
        ) : null}
      </TechnicalDetail>
    </div>
    </Skeletonizer>
  )
}

function verdict(r: AdminRegistration): { meaning: Meaning; note: string } {
  switch (r.nameVerdict) {
    case 'Available': return { meaning: 'done', note: 'available' }
    case 'ManualDecision': return { meaning: r.lodgedAt ? 'waiting' : 'action', note: 'ASIC to decide' }
    case 'NotAvailable': return { meaning: 'action', note: 'taken' }
    default: return { meaning: 'quiet', note: r.paidAt ? 'not yet' : '—' }
  }
}
