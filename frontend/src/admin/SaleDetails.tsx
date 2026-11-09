import { type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { KeyRound, Loader2, RefreshCw, Undo2 } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../api/client'
import { HANDLED, Status, renewalStatus, statusOf, type StatusEntry } from './_status'
import { DueIn, When } from './_rows'
import { NextStep, RecordCrumbs, RecordHeader, StoryStrip, TechnicalDetail, type StoryStep, DetailRow } from './_record'
import { fmtDateTime, fmtMoney2, formatAbn } from './_utils'
import { Skeletonizer } from '@/components/skeletonizer'
import { sample } from '@/components/skeleton-samples'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

type Detail = Awaited<ReturnType<typeof api.admin.ontraportSale>>

const shortDate = (s: string) => new Date(s).toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })

// The page drawn as a skeleton while the sale loads.
const SAMPLE_SALE: Detail = {
  id: sample.id(0),
  client: { id: sample.id(1), fullName: sample.name(3) },
  contactName: sample.name(3),
  email: sample.email(3),
  mobileNumber: sample.mobile(),
  dateOfBirth: '1979-11-02',
  businessName: sample.business(3),
  abn: sample.abn(3),
  businessNameOwner: sample.name(3),
  address: '14 Harbour Street, Wollongong NSW 2500',
  renewalYears: 3,
  amountPaid: 299,
  renewalDueDate: sample.daysAhead(25),
  status: 'Completed',
  syncedAt: sample.daysAgo(4),
  processedAt: sample.daysAgo(4),
  errorMessage: null,
  handledAt: null,
  handledNote: null,
  ontraportContactId: '184522',
  ontraportTransactionId: '99231',
  renewal: {
    id: sample.id(2), status: 'Completed', errorCategory: null, nextRetryAt: null, initiatedAt: sample.daysAgo(4),
    completedAt: sample.daysAgo(4), bpayReference: null, bpayPaidAt: null, transactionReference: '7310 2841 9922',
  },
  keyRequests: [],
  keyLetters: [{ id: sample.id(3), status: 'Completed', receivedAt: sample.daysAgo(1), hasKey: true }],
}

export default function SaleDetails() {
  const { id } = useParams()
  const queryClient = useQueryClient()
  const { data: loaded, isPending, isError } = useQuery({
    queryKey: ['admin-ontraport-sale', id],
    queryFn: () => api.admin.ontraportSale(id!),
    enabled: !!id,
  })
  const data = loaded ?? (isPending ? SAMPLE_SALE : undefined)

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['admin-ontraport-sale', id] })
    void queryClient.invalidateQueries({ queryKey: ['admin-ontraport-sales'] })
    void queryClient.invalidateQueries({ queryKey: ['admin-today'] })
    void queryClient.invalidateQueries({ queryKey: ['admin-sidebar-counts'] })
  }
  const requeue = useMutation({ mutationFn: () => api.admin.requeueOntraportSale(id!), onSettled: invalidate })
  const keyRequest = useMutation({ mutationFn: () => api.admin.requestAsicKeyForSale(id!), onSettled: invalidate })
  const unhandle = useMutation({ mutationFn: () => api.admin.unmarkSaleHandled(id!), onSettled: invalidate })

  const tryAgain = () => void sileo.promise(requeue.mutateAsync(), {
    loading: { title: 'Putting the sale back in the queue…' },
    success: (r) => ({ title: r.message }),
    error: (e) => ({ title: 'Not queued', description: e instanceof Error ? e.message : undefined }),
  }).catch(() => {})
  const askForKey = () => void sileo.promise(keyRequest.mutateAsync(), {
    loading: { title: 'Adding to the key requests…' },
    success: () => ({ title: 'Added to the ASIC key requests.' }),
    error: (e) => ({ title: 'Couldn’t add it', description: e instanceof Error ? e.message : undefined }),
  }).catch(() => {})
  const putBack = () => void sileo.promise(unhandle.mutateAsync(), {
    loading: { title: 'Putting it back…' },
    success: () => ({ title: 'Back on the work queues.' }),
    error: (e) => ({ title: 'Didn’t change', description: e instanceof Error ? e.message : undefined }),
  }).catch(() => {})

  if (isError || !data) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 text-center sm:px-6 lg:px-8">
        <h3 className="text-heading">Sale not found</h3>
        <Button asChild variant="ghost" size="sm" className="mt-4"><Link to="/admin/ontraport-sales">Back to Ontraport sales</Link></Button>
      </div>
    )
  }

  const status = saleStatus(data)
  const tryAgainButton = (
    <Button size="sm" onClick={tryAgain} disabled={requeue.isPending}>
      {requeue.isPending ? <Loader2 className="animate-spin" /> : <RefreshCw />}Try again
    </Button>
  )
  const undoButton = (
    <Button size="sm" variant="outline" onClick={putBack} disabled={unhandle.isPending}>
      <Undo2 />Undo
    </Button>
  )
  const keyButton = (
    <Button size="sm" variant="outline" onClick={askForKey} disabled={keyRequest.isPending}>
      <KeyRound />Request ASIC key
    </Button>
  )

  return (
    <Skeletonizer loading={isPending}>
    <div className="mx-auto max-w-5xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <RecordCrumbs client={data.client} businessName={data.businessName}
        list={{ to: '/admin/ontraport-sales', label: 'Ontraport sales' }} here={`Sale of ${shortDate(data.syncedAt)}`} />

      <RecordHeader
        title={headline(data)}
        sub={<>{data.businessName} · ABN {formatAbn(data.abn)}{data.renewalDueDate ? <> · due <DueIn at={data.renewalDueDate} /></> : ' · no due date'} · synced <When at={data.syncedAt} /></>}
        status={status}
      >
        {nextStep(data, tryAgainButton, keyButton, undoButton)}
        <StoryStrip steps={story(data)} />
      </RecordHeader>

      <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-2">
        <Section title="Who bought it">
          {data.client ? (
            <DetailRow label="Client" value={<Link to={`/admin/clients/${data.client.id}`} className="font-medium text-link hover:underline">{data.client.fullName || 'Unnamed client'}</Link>} />
          ) : null}
          <DetailRow label="Name" value={data.contactName || '—'} />
          <DetailRow label="Email" value={data.email || '—'} />
          <DetailRow label="Mobile" value={data.mobileNumber || '—'} mono />
          {data.businessNameOwner ? <DetailRow label="Name holder" value={data.businessNameOwner} /> : null}
        </Section>

        <Section title="ASIC key for this name">
          {data.keyLetters.length === 0 && data.keyRequests.length === 0 ? (
            <p className="text-sm text-muted-foreground">No key requested or received yet.</p>
          ) : null}
          {data.keyLetters.map((k) => (
            <DetailRow key={k.id} label="Key letter" value={<Link to={`/admin/asic-keys/${k.id}`} className="flex flex-wrap items-center gap-2 text-caption text-muted-foreground hover:underline"><Status kind="keyLetter" status={k.status} /><When at={k.receivedAt} /></Link>} />
          ))}
          {data.keyRequests.map((k) => (
            <DetailRow key={k.id} label="Key request" value={<Link to={`/admin/asic-key-requests/${k.id}`} className="flex flex-wrap items-center gap-2 text-caption text-muted-foreground hover:underline"><Status kind="keyRequest" status={k.status} /><When at={k.createdAt} />{k.referenceNumber ? ` · ref ${k.referenceNumber}` : ''}</Link>} />
          ))}
        </Section>
      </div>

      <TechnicalDetail>
        <dl className="grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-2">
          <DetailRow label="Sale ID" value={<span className="break-all">{data.id}</span>} mono />
          <DetailRow label="Status" value={data.status} />
          <DetailRow label="Ontraport contact" value={data.ontraportContactId} mono />
          <DetailRow label="Transaction" value={data.ontraportTransactionId} mono />
          <DetailRow label="Synced" value={fmtDateTime(data.syncedAt)} />
          <DetailRow label="Processed" value={data.processedAt ? fmtDateTime(data.processedAt) : '—'} />
          {data.handledAt ? <DetailRow label="Handled" value={fmtDateTime(data.handledAt)} /> : null}
          <DetailRow label="Due date" value={data.renewalDueDate ? fmtDateTime(data.renewalDueDate) : '—'} />
          <DetailRow label="Date of birth" value={data.dateOfBirth ?? '—'} />
          <DetailRow label="Address" value={data.address || '—'} />
          {data.renewal ? <DetailRow label="Renewal" value={<Link to={`/admin/renewals/${data.renewal.id}`} className="break-all text-link hover:underline">{data.renewal.id}</Link>} mono /> : null}
        </dl>
        {data.errorMessage ? (
          <div className="mt-4">
            <div className="text-caption text-muted-foreground">Last error</div>
            <pre className="mt-1 whitespace-pre-wrap break-words rounded-md border bg-muted/50 p-3 text-caption">{data.errorMessage}</pre>
          </div>
        ) : null}
      </TechnicalDetail>
    </div>
    </Skeletonizer>
  )
}

/** The sale's own status; once it started a renewal, the renewal's status says more. */
function saleStatus(d: Detail): StatusEntry {
  if (d.handledAt && d.renewal?.status !== 'Completed') return HANDLED
  if (d.renewal && d.status !== 'IneligibleForRenewal') return renewalStatus(d.renewal)
  return statusOf('sale', d.status)
}

function headline(d: Detail): string {
  const who = d.contactName || 'The client'
  const r = d.renewal
  if (d.handledAt && r?.status !== 'Completed') return `${who} paid; handled outside Renewtron`
  if (r?.status === 'Completed') return r.bpayReference && !r.bpayPaidAt ? `Renewed; ASIC’s BPAY bill still to pay` : `Paid and renewed`
  if (r && (r.status === 'Pending' || r.status === 'Processing')) return 'Paid; the renewal is going through'
  switch (d.status) {
    case 'Synced': return `${who} paid; the renewal starts on the next run`
    case 'WaitingForRenewalWindow': return `${who} paid early; it renews when ASIC opens the window`
    case 'RenewalQueued': return 'Paid; the renewal is queued'
    case 'RenewalFailed': return `${who} paid, and the renewal failed`
    case 'IneligibleForRenewal': return `${who} paid, but this name can’t be renewed with us`
    case 'NotDueForRenewal': return 'Paid, but the name isn’t due for renewal'
    case 'AsicNotYetDue': return 'Paid; ASIC says it isn’t due yet'
    case 'RenewalInProgress': return 'Paid; ASIC has a renewal open for this ABN'
    default: return statusOf('sale', d.status).label
  }
}

function nextStep(d: Detail, tryAgain: ReactNode, keyButton: ReactNode, undoButton: ReactNode): ReactNode {
  const openClient = d.client ? <Button asChild size="sm" variant="outline"><Link to={`/admin/clients/${d.client.id}`}>Open client</Link></Button> : null
  const r = d.renewal
  if (d.handledAt && r?.status !== 'Completed') {
    return (
      <NextStep meaning="done" title="Nothing to do; it was handled outside Renewtron." actions={undoButton}>
        {d.handledNote ?? <>Marked handled <When at={d.handledAt} />.</>}
      </NextStep>
    )
  }
  if (r) {
    const s = renewalStatus(r)
    if (r.status === 'Completed') {
      const toPay = r.bpayReference && !r.bpayPaidAt
      return (
        <NextStep meaning={toPay ? 'action' : d.keyLetters.some((k) => k.hasKey) ? 'done' : 'quiet'}
          title={toPay ? 'Pay ASIC’s BPAY bill.' : d.keyLetters.some((k) => k.hasKey) ? 'Nothing to do.' : 'Renewed. Get the ASIC key if the client needs it.'}
          actions={toPay ? <Button asChild size="sm"><Link to="/admin/bpay">Pay in BPAY to pay</Link></Button> : d.keyRequests.length === 0 ? keyButton : openClient}>
          Renewed <When at={r.completedAt ?? r.initiatedAt} />{r.transactionReference ? `, ASIC reference ${r.transactionReference}` : ''}.
        </NextStep>
      )
    }
    return (
      <NextStep meaning={s.meaning} title={s.meaning === 'action' ? 'The renewal needs a person.' : 'Nothing to do here; follow the renewal.'}
        actions={<Button asChild size="sm"><Link to={`/admin/renewals/${r.id}`}>Open renewal</Link></Button>}>
        Renewal: {s.label.toLowerCase()}, started <When at={r.initiatedAt} />.
      </NextStep>
    )
  }
  switch (d.status) {
    case 'RenewalFailed':
      return <NextStep meaning="action" title="Try again puts it back for the next processing run." actions={<>{tryAgain}{openClient}</>}><span title={d.errorMessage ?? undefined}>{truncate(d.errorMessage ?? 'No error was recorded.', 120)}</span></NextStep>
    case 'IneligibleForRenewal':
      return <NextStep meaning="action" title="Decide: refund, or contact the client." actions={openClient}><span title={d.errorMessage ?? undefined}>{truncate(d.errorMessage ?? 'Renewtron won’t retry it.', 120)}</span></NextStep>
    case 'WaitingForRenewalWindow':
    case 'AsicNotYetDue':
    case 'RenewalInProgress':
      return <NextStep meaning="waiting" title="Nothing to do; Renewtron tries again on its own." actions={openClient}>{statusOf('sale', d.status).hint}</NextStep>
    case 'NotDueForRenewal':
      return <NextStep meaning="quiet" title="Check the due date." actions={openClient}><span title="The due date is outside ASIC’s renewal window, so Renewtron won’t renew it. If it’s wrong in Ontraport, fix it there and sync.">It’s outside ASIC’s renewal window; fix it in Ontraport if wrong.</span></NextStep>
    default:
      return <NextStep meaning="working" title="Nothing to do yet.">The next processing run (07:00) starts the renewal.</NextStep>
  }
}

function truncate(s: string, n: number) {
  return s.length > n ? s.slice(0, n - 1) + '…' : s
}

/** Sale → renewal → ASIC fee → ASIC key. */
function story(d: Detail): StoryStep[] {
  const steps: StoryStep[] = [{ label: `Paid ${fmtMoney2(d.amountPaid)}`, meaning: 'done', at: d.syncedAt, note: undefined, current: true }]
  const r = d.renewal
  if (r) {
    const s = renewalStatus(r)
    steps.push({ label: `Renewal: ${s.label.toLowerCase()}`, meaning: s.meaning, at: r.completedAt ?? r.initiatedAt, to: `/admin/renewals/${r.id}` })
    if (r.bpayReference) {
      steps.push(r.bpayPaidAt
        ? { label: 'ASIC fee paid', meaning: 'done', at: r.bpayPaidAt, to: '/admin/bpay' }
        : { label: 'ASIC fee', meaning: r.status === 'Completed' ? 'action' : 'quiet', note: 'BPAY to pay', to: '/admin/bpay' })
    }
  } else if (d.handledAt) {
    steps.push({ label: 'Renewal', meaning: 'done', note: 'handled outside Renewtron', at: undefined })
  } else {
    const s = statusOf('sale', d.status)
    steps.push({ label: 'Renewal', meaning: s.meaning === 'done' ? 'done' : s.meaning, note: s.label.toLowerCase() })
  }
  const letter = d.keyLetters.find((k) => k.hasKey) ?? d.keyLetters[0]
  const request = d.keyRequests[0]
  if (letter) {
    const s = statusOf('keyLetter', letter.status)
    steps.push({ label: 'ASIC key', meaning: s.meaning, at: letter.receivedAt, note: s.label.toLowerCase(), to: `/admin/asic-keys/${letter.id}` })
  } else if (request) {
    const s = statusOf('keyRequest', request.status)
    steps.push({ label: 'ASIC key', meaning: s.meaning, note: s.label.toLowerCase(), to: `/admin/asic-key-requests/${request.id}` })
  } else {
    steps.push({ label: 'ASIC key', meaning: 'quiet', note: 'not requested' })
  }
  return steps
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Card className="gap-4">
      <CardHeader><CardTitle>{title}</CardTitle></CardHeader>
      <CardContent><dl className="space-y-3">{children}</dl></CardContent>
    </Card>
  )
}

