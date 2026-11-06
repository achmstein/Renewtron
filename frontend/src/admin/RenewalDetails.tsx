import { type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Loader2, RefreshCw, Undo2 } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../api/client'
import { StatusPill } from './_ui'
import { renewalStatus, statusOf, type StatusEntry } from './_status'
import { DueIn, When, agoWords, dueWords } from './_rows'
import { NextStep, RecordCrumbs, RecordHeader, StoryStrip, TechnicalDetail, type StoryStep, DetailRow } from './_record'
import { fmtDateTime, fmtMoney2, formatAbn } from './_utils'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

type Detail = Awaited<ReturnType<typeof api.admin.renewal>>

function fmtDob(s: string) {
  const d = new Date(s)
  return `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}/${d.getFullYear()}`
}

const paidByClient = (d: Detail) => d.paymentType === 'External' || d.stripePayment?.paymentStatus === 'succeeded'
/** ASIC may already hold money for this run: retrying could pay twice. Same rule as the retry endpoint. */
const paymentAtRisk = (d: Detail) => d.errorCategory !== 'CardDeclined'
  && (d.failedAtStep === 'Complete Payment Action' || !!d.hostedTokenizationId || d.errorCategory === 'PaymentRisk')

/** The renewal's status, refined for a stuck run. */
function statusFor(d: Detail): StatusEntry {
  if (d.handledAt && d.status !== 'Completed') return renewalStatus(d)
  if (d.stuck) return { label: 'Stuck', meaning: 'action', hint: 'Nothing has happened for over 2 hours.' }
  return renewalStatus(d)
}

export default function RenewalDetails() {
  const { id } = useParams()
  const queryClient = useQueryClient()

  const { data, isPending } = useQuery({
    queryKey: ['admin-renewal', id],
    queryFn: () => api.admin.renewal(id!),
    enabled: !!id,
    // Auto-refresh while queued or processing (manual renewals land here as Pending)
    refetchInterval: (query) => (query.state.data?.status === 'Processing' || query.state.data?.status === 'Pending' ? 3000 : false),
  })

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['admin-renewal', id] })
    void queryClient.invalidateQueries({ queryKey: ['admin-renewals'] })
    void queryClient.invalidateQueries({ queryKey: ['admin-today'] })
    void queryClient.invalidateQueries({ queryKey: ['admin-sidebar-counts'] })
  }
  const retryMutation = useMutation({ mutationFn: () => api.admin.retryRenewal(id!), onSettled: invalidate })
  const bpayMutation = useMutation({ mutationFn: (paid: boolean) => api.admin.markBpayPaid(id!, paid), onSettled: invalidate })
  const unhandle = useMutation({ mutationFn: () => api.admin.unmarkRenewalHandled(id!), onSettled: invalidate })

  const retry = () => {
    void sileo.promise(retryMutation.mutateAsync(), {
      loading: { title: 'Retrying renewal…' },
      success: (r) => ({ title: r.message ?? 'Renewal queued for retry.' }),
      error: (e) => ({ title: 'Retry refused', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }
  const markBpay = (paid: boolean) => {
    void sileo.promise(bpayMutation.mutateAsync(paid), {
      loading: { title: paid ? 'Marking BPAY paid…' : 'Undoing…' },
      success: () => ({ title: paid ? 'Marked BPAY paid.' : 'Marked BPAY unpaid.' }),
      error: (e) => ({ title: 'Couldn’t update it', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const putBack = () => {
    void sileo.promise(unhandle.mutateAsync(), {
      loading: { title: 'Putting it back…' },
      success: () => ({ title: 'Back on the work queues.' }),
      error: (e) => ({ title: 'Didn’t change', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  if (isPending) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 text-center sm:px-6 lg:px-8">
        <Loader2 className="mx-auto size-6 animate-spin text-muted-foreground" />
      </div>
    )
  }

  if (!data) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 text-center sm:px-6 lg:px-8">
        <h3 className="text-heading">Renewal not found</h3>
        <Button asChild variant="ghost" size="sm" className="mt-4"><Link to="/admin/renewals">Back to renewals</Link></Button>
      </div>
    )
  }

  const status = statusFor(data)
  const retryButton = (
    <Button size="sm" onClick={retry} disabled={retryMutation.isPending}>
      {retryMutation.isPending ? <Loader2 className="animate-spin" /> : <RefreshCw />}Retry renewal
    </Button>
  )
  const undoButton = (
    <Button size="sm" variant="outline" onClick={putBack} disabled={unhandle.isPending}>
      <Undo2 />Undo
    </Button>
  )
  const next = data.handledAt && data.status !== 'Completed'
    ? (
      <NextStep meaning="done" title="Nothing to do; it was handled outside Renewtron." actions={undoButton}>
        {data.handledNote ?? <>Marked handled <When at={data.handledAt} />.</>}
      </NextStep>
    )
    : nextStep(data, retryButton, markBpay, bpayMutation.isPending)

  return (
    <div className="mx-auto max-w-5xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <RecordCrumbs
        client={data.client}
        businessName={data.businessName}
        list={{ to: '/admin/renewals', label: 'Renewals' }}
        here={`Renewal of ${new Date(data.initiatedAt).toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })}`}
      />

      <RecordHeader
        title={headline(data)}
        sub={<>{data.businessName} · ABN {formatAbn(data.abn)}{data.dueDate ? <> · due <DueIn at={data.dueDate} /></> : null} · started <When at={data.initiatedAt} /></>}
        status={status}
      >
        {next}
        <StoryStrip steps={story(data, status)} />
      </RecordHeader>

      <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-2">
        <Section title="Who it’s for">
          {data.client ? (
            <DetailRow label="Client" value={<Link to={`/admin/clients/${data.client.id}`} className="font-medium text-link hover:underline">{data.client.fullName || 'Unnamed client'}</Link>} />
          ) : null}
          {data.lead ? (
            <DetailRow label="Wizard visit" value={<Link to={`/admin/leads/${data.lead.id}`} className="text-link hover:underline">{data.lead.fullName} · {data.lead.email}</Link>} />
          ) : null}
          <DetailRow label="Email" value={data.email ?? '—'} />
          <DetailRow label="Mobile" value={data.mobileNumber ?? '—'} mono />
          <DetailRow label="Date of birth" value={data.dateOfBirth ? fmtDob(data.dateOfBirth) : '—'} />
        </Section>

        <Section title="How it was paid">
          <DetailRow label="Client paid" value={<span className="tabular-nums">{fmtMoney2(data.amount)}</span>} />
          <DetailRow label="Through" value={data.paymentType === 'External'
            ? (data.sale ? <>Ontraport, <When at={data.sale.syncedAt} /></> : 'Outside Renewtron')
            : <>Card via Stripe · {statusOf('payment', data.stripePayment?.paymentStatus).label.toLowerCase()}{data.stripePayment?.paidAt ? <>, <When at={data.stripePayment.paidAt} /></> : null}</>} />
          {data.bpayReference ? (
            <DetailRow label="ASIC fee" value={
              <span className="flex flex-wrap items-center gap-2">
                {data.bpayPaidAt
                  ? <><StatusPill tone="emerald">BPAY paid</StatusPill><span className="text-caption text-muted-foreground"><When at={data.bpayPaidAt} /></span></>
                  : <StatusPill tone="red">BPAY to pay</StatusPill>}
                <Button size="sm" variant={data.bpayPaidAt ? 'ghost' : 'outline'} className="sm:h-6 sm:px-2 sm:text-xs" disabled={bpayMutation.isPending} onClick={() => markBpay(!data.bpayPaidAt)}>
                  {data.bpayPaidAt ? 'Undo' : 'Mark paid'}
                </Button>
              </span>
            } />
          ) : data.status === 'Completed' ? (
            <DetailRow label="ASIC fee" value="Paid by card at lodgement" />
          ) : null}
          {data.transactionReference ? <DetailRow label="ASIC reference" value={data.transactionReference} mono /> : null}
        </Section>
      </div>

      <TechnicalDetail defaultOpen={false}>
        <dl className="grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-2">
          <DetailRow label="Request ID" value={<span className="break-all">{data.id}</span>} mono />
          <DetailRow label="Status" value={`${data.status}${data.errorCategory ? ` · ${data.errorCategory}` : ''}`} />
          <DetailRow label="Failed at step" value={data.failedAtStep ?? '—'} />
          <DetailRow label="Attempts" value={`${data.attemptCount || 1}${data.cardFailureCount ? ` · card failed ${data.cardFailureCount}×` : ''}`} />
          <DetailRow label="Last run" value={data.lastAttemptAt ? fmtDateTime(data.lastAttemptAt) : '—'} />
          <DetailRow label="Next auto-retry" value={data.nextRetryAt ? fmtDateTime(data.nextRetryAt) : '—'} />
          <DetailRow label="Initiated" value={fmtDateTime(data.initiatedAt)} />
          <DetailRow label="Completed" value={data.completedAt ? fmtDateTime(data.completedAt) : '—'} />
          {data.handledAt ? <DetailRow label="Handled" value={fmtDateTime(data.handledAt)} /> : null}
          <DetailRow label="Source" value={`${data.source} · started by ${data.initiatedByLabel}`} />
          <DetailRow label="ASIC account no." value={data.accountNumber} mono />
          <DetailRow label="Hosted token" value={<span className="break-all">{data.hostedTokenizationId ?? '—'}</span>} mono />
          <DetailRow label="IP address" value={data.ipAddress ?? '—'} mono />
          {data.bpayReference ? <DetailRow label="BPAY" value={`biller ${data.bpayBillerCode ?? '—'} · ref ${data.bpayReference}`} mono /> : null}
          {data.stripePayment ? <DetailRow label="Payment intent" value={<span className="break-all">{data.stripePayment.paymentIntentId}</span>} mono /> : null}
          {data.stripePayment?.cardLast4 ? (
            <DetailRow label="Client’s card" value={`${data.stripePayment.cardBrand?.toUpperCase() ?? ''} •••• ${data.stripePayment.cardLast4} · ${data.stripePayment.cardExpMonth ?? '--'}/${data.stripePayment.cardExpYear ?? '----'} · ${data.stripePayment.cardholderName ?? ''}`} />
          ) : null}
        </dl>
        {data.errorMessage ? (
          <div className="mt-4">
            <div className="text-caption text-muted-foreground">Last error</div>
            <pre className="mt-1 whitespace-pre-wrap break-words rounded-md border bg-muted/50 p-3 text-caption">{data.errorMessage}</pre>
          </div>
        ) : null}
      </TechnicalDetail>
    </div>
  )
}

/** The page title: what happened to this renewal, in words. */
function headline(d: Detail): string {
  if (d.handledAt && d.status !== 'Completed') return 'Handled outside Renewtron'
  const years = `${d.renewalYears} year${d.renewalYears === 1 ? '' : 's'}`
  if (d.stuck) return 'Renewal stuck: nothing has moved for over 2 hours'
  switch (d.status) {
    case 'Completed': return d.bpayReference && !d.bpayPaidAt ? `Renewed for ${years}; ASIC’s BPAY bill still to pay` : `Renewed for ${years}`
    case 'Processing': return 'Lodging with ASIC now'
    case 'Pending': return 'Queued to lodge with ASIC'
  }
  if (!paidByClient(d)) return 'Not renewed: the client’s card payment didn’t go through'
  if (d.nextRetryAt) return `Failed; Renewtron retries ${dueWords(d.nextRetryAt)}`
  if (d.errorCategory === 'NotDueYet') return 'Not renewed yet: ASIC says it isn’t due'
  if (paymentAtRisk(d)) return 'Failed at the payment step: check ASIC before retrying'
  if (d.errorCategory === 'AlreadyInProgress') return 'Blocked: ASIC has a renewal open for this ABN'
  if (d.errorCategory === 'CardDeclined') return 'Not renewed: ASIC declined our card (no money taken)'
  if (d.errorCategory === 'Terminal') return 'Refused by ASIC: the details didn’t pass its checks'
  return d.failedAtStep ? `Renewal failed at “${d.failedAtStep}”` : 'Renewal failed'
}

/** The next-step block: who has to act and the button that does it. */
function nextStep(d: Detail, retryButton: ReactNode, markBpay: (paid: boolean) => void, busy: boolean): ReactNode {
  const toClient = d.client ? <Button asChild size="sm" variant="outline"><Link to={`/admin/clients/${d.client.id}`}>Open client</Link></Button> : null
  if (d.stuck) {
    return (
      <NextStep meaning="action" title={`Last activity ${agoWords(d.lastAttemptAt ?? d.initiatedAt)}.`}
        actions={<Button asChild size="sm" variant="outline"><Link to="/admin/renewals">Reconcile on Renewals</Link></Button>}>
        <span title="The reconcile sweep re-queues stale renewals. If it stays stuck, look the name up at ASIC before running it again: it may have lodged.">Check ASIC before running it again: it may have lodged.</span>
      </NextStep>
    )
  }
  switch (d.status) {
    case 'Completed':
      if (d.bpayReference && !d.bpayPaidAt) {
        return (
          <NextStep meaning="action" title="ASIC holds the renewal until its BPAY bill is paid."
            actions={<>
              <Button asChild size="sm"><Link to="/admin/bpay">Pay in BPAY to pay</Link></Button>
              <Button size="sm" variant="outline" disabled={busy} onClick={() => markBpay(true)}>Paid at the bank</Button>
            </>}>
            Lodged <When at={d.completedAt ?? d.initiatedAt} />{d.transactionReference ? `, ASIC reference ${d.transactionReference}` : ''}.
          </NextStep>
        )
      }
      return (
        <NextStep meaning="done" title="Nothing to do." actions={toClient}>
          ASIC lodged it <When at={d.completedAt ?? d.initiatedAt} />{d.transactionReference ? `, reference ${d.transactionReference}` : ''}.
        </NextStep>
      )
    case 'Processing':
      return <NextStep meaning="working" title="Nothing to do yet.">This page updates by itself while the renewal goes through ASIC.</NextStep>
    case 'Pending':
      return <NextStep meaning="working" title="Nothing to do yet.">The renewal worker picks it up shortly; this page updates by itself.</NextStep>
  }
  if (!paidByClient(d)) {
    return <NextStep meaning="quiet" title="Nothing to deliver." actions={toClient}>No money of theirs was taken. They need to pay again to renew.</NextStep>
  }
  if (d.nextRetryAt) {
    return (
      <NextStep meaning="waiting" title={`Renewtron tries again ${dueWords(d.nextRetryAt)}.`} actions={retryButton}>
        <span title={d.errorMessage ?? undefined}>You can retry now instead.</span>
      </NextStep>
    )
  }
  if (d.errorCategory === 'NotDueYet') {
    return <NextStep meaning="waiting" title="Try again once the name is inside ASIC’s renewal window." actions={retryButton}>The client has paid; nothing is lost by waiting.</NextStep>
  }
  if (paymentAtRisk(d)) {
    return (
      <NextStep meaning="action" title="Look the name up at ASIC first." actions={toClient}>
        <span title="This run reached ASIC’s payment page, so ASIC may already have our money and the renewal. Retrying here is blocked so it can’t pay twice; if ASIC shows it renewed, note it on the client.">ASIC may already have our money, so retrying is blocked.</span>
      </NextStep>
    )
  }
  return (
    <NextStep meaning="action" title="Retry runs the renewal again from the start." actions={<>{retryButton}{toClient}</>}>
      <span title={d.errorMessage ?? undefined}>{d.errorMessage ? truncate(d.errorMessage, 120) : 'No error was recorded.'}</span>
    </NextStep>
  )
}

/** Visit → paid → renewal → ASIC fee: the records either side of this one. */
function story(d: Detail, status: StatusEntry): StoryStep[] {
  const steps: StoryStep[] = []
  if (d.sale) {
    steps.push({ label: 'Ontraport sale', meaning: 'done', at: d.sale.syncedAt, to: `/admin/ontraport-sales/${d.sale.id}` })
  } else {
    steps.push({ label: d.initiatedByLabel === 'Customer' ? 'Wizard search' : 'Name search', meaning: 'done', at: d.searchedAt, to: `/admin/searches/${d.searchLogId}` })
    steps.push(d.paymentType === 'External'
      ? { label: 'Paid', meaning: 'done', note: 'outside Renewtron' }
      : paidByClient(d)
        ? { label: `Paid ${fmtMoney2(d.amount)}`, meaning: 'done', at: d.stripePayment?.paidAt ?? d.initiatedAt }
        : { label: 'Payment', meaning: 'quiet', note: statusOf('payment', d.stripePayment?.paymentStatus).label.toLowerCase() })
  }
  steps.push({ label: `Renewal: ${status.label.toLowerCase()}`, meaning: status.meaning, at: d.completedAt ?? d.lastAttemptAt ?? d.initiatedAt, current: true })
  if (d.bpayReference) {
    steps.push(d.bpayPaidAt
      ? { label: 'ASIC fee paid', meaning: 'done', at: d.bpayPaidAt, to: '/admin/bpay' }
      : { label: 'ASIC fee', meaning: d.status === 'Completed' ? 'action' : 'quiet', note: 'BPAY to pay', to: '/admin/bpay' })
  }
  return steps
}

function truncate(s: string, n: number) {
  return s.length > n ? s.slice(0, n - 1) + '…' : s
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Card className="gap-4">
      <CardHeader><CardTitle>{title}</CardTitle></CardHeader>
      <CardContent><dl className="space-y-3">{children}</dl></CardContent>
    </Card>
  )
}

