import { type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api, type RenewalOrderAttempt, type RenewalOrderDecision, type RenewalOrderRow } from '../api/client'
import { Status, inSentence, orderStatus, renewalStatus } from './_status'
import { QueueNav } from './_queue'
import { actionsFor, OrderActionButtons, OrderActionsProvider } from './_orderActions'
import { DueIn, When } from './_rows'
import { NextStep, RecordCrumbs, RecordHeader, TechnicalDetail, DetailRow } from './_record'
import { JourneyTimeline } from './_journey'
import { fmtDateTime, fmtMoney2, formatAbn } from './_utils'
import { Skeletonizer } from '@/components/skeletonizer'
import { sample } from '@/components/skeleton-samples'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { pageClass } from './_ui'

/**
 * One paid renewal, in the shape every details page shares: what happened in a sentence, the next
 * step with its button, the story (visit → paid → lodged → renewed or closed), what ASIC said, and
 * the ASIC runs and technical detail below.
 */
type OrderData = Awaited<ReturnType<typeof api.admin.renewalOrder>>

// The page drawn as a skeleton while the renewal loads.
const SAMPLE_ORDER: OrderData = {
  order: {
    id: sample.id(0), source: 'Ontraport', sourceId: sample.id(1), clientId: sample.id(2), payerName: sample.name(4), clientName: sample.name(4),
    businessName: sample.business(4), abn: sample.abn(4), dueDate: sample.daysAhead(18), years: 3, amountPaid: 299, paidAt: sample.daysAgo(5),
    stage: 'Renewed', reason: null, note: null, nextActionAt: null, stageChangedAt: sample.daysAgo(4), attemptCount: 1,
    currentAttemptId: sample.id(3), renewedAt: sample.daysAgo(4), canRetry: false,
  },
  client: { id: sample.id(2), fullName: sample.name(4) },
  source: { label: 'Ontraport sale', to: '/admin/ontraport-sales' },
  attempts: [{
    id: sample.id(3), status: 'Completed', errorCategory: null, failedAtStep: null, errorMessage: null, initiatedAt: sample.daysAgo(4),
    lastAttemptAt: sample.daysAgo(4), completedAt: sample.daysAgo(4), nextRetryAt: null, attemptCount: 1, paysBy: 'Card',
    bpayPaidAt: null, transactionReference: '7310 2841 9922', current: true,
  }],
  decision: { outcome: null, at: null, by: null, evidence: null, evidenceSource: null, evidenceAt: null, reference: null, asicCheckedAt: null },
  visit: null,
  canFixName: false,
}

const EVIDENCE_FROM: Record<NonNullable<RenewalOrderDecision['evidenceSource']>, string> = {
  AsicRegister: 'ASIC’s register file',
  AsicRenewalPage: 'ASIC’s renewal page',
  Staff: 'A person',
  Ontraport: 'Ontraport',
}

export default function RenewalOrderDetails() {
  const { id } = useParams()
  const { data: loaded, isPending, isError } = useQuery({
    queryKey: ['admin-renewal-order', id],
    queryFn: () => api.admin.renewalOrder(id!),
    enabled: !!id,
    refetchInterval: (q) => (q.state.data?.order.stage === 'Renewing' ? 5000 : false),
  })
  const data = loaded ?? (isPending ? SAMPLE_ORDER : undefined)
  if (isError || !data) {
    return (
      <div className={pageClass('py-12 text-center')}>
        <h3 className="text-heading">Renewal not found</h3>
        <Button asChild variant="ghost" size="sm" className="mt-4"><Link to="/admin/renewal-orders">Back to renewals</Link></Button>
      </div>
    )
  }

  const o = data.order
  const d = data.decision
  const status = orderStatus(o.stage, o.reason)
  const who = data.client?.fullName || o.clientName || o.payerName || 'The client'
  // The renewal's actions, the usual fix first, with the link to where it was paid beside them.
  const actions = (
    <OrderActionButtons order={o} actions={actionsFor(o, !!d.outcome)} layout="page"
      extra={<Button asChild size="sm" variant="outline"><Link to={data.source.to}>{SOURCE_BUTTON[o.source]}</Link></Button>} />
  )

  return (
    <OrderActionsProvider>
    <Skeletonizer loading={isPending}>
    <div className={pageClass()}>
      <QueueNav id={id} to={orderPage} />
      <RecordCrumbs client={data.client} businessName={o.businessName}
        list={{ to: '/admin/renewal-orders', label: 'Renewals' }} here={`Paid ${new Date(o.paidAt).toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })}`} />

      <RecordHeader
        title={headline(o, who)}
        sub={<>{o.businessName} · ABN {formatAbn(o.abn)}{o.dueDate ? <> · due <DueIn at={o.dueDate} /></> : ' · no due date'} · {fmtMoney2(o.amountPaid)} for {o.years} year{o.years === 1 ? '' : 's'}</>}
        status={status}
      >
        {nextStep(o, d, status.hint, actions)}
      </RecordHeader>

      <JourneyTimeline kind="order" id={id} className="mt-6" />


      {d.evidence || d.asicCheckedAt ? <AsicSaid d={d} /> : null}

      <Card className="mt-6 gap-0 py-0">
        <CardHeader className="border-b py-4"><CardTitle>ASIC runs</CardTitle></CardHeader>
        <CardContent className="p-0">
          {data.attempts.length === 0
            ? <p className="px-4 py-4 text-sm text-muted-foreground sm:px-6">No renewal started at ASIC yet.</p>
            : data.attempts.map((a) => <AttemptRow key={a.id} a={a} />)}
        </CardContent>
      </Card>

      <TechnicalDetail>
        <dl className="grid grid-cols-1 gap-x-8 gap-y-3 text-sm lg:grid-cols-2">
          <DetailRow label="Order" value={<span className="break-all font-mono">{o.id}</span>} />
          <DetailRow label="Stage" value={`${o.stage}${o.reason ? ` · ${o.reason}` : ''}`} />
          <DetailRow label="Paid through" value={data.source.label} />
          <DetailRow label="Paid" value={fmtDateTime(o.paidAt)} />
          <DetailRow label="Stage since" value={fmtDateTime(o.stageChangedAt)} />
          <DetailRow label="Next on its own" value={o.nextActionAt ? fmtDateTime(o.nextActionAt) : '—'} />
          {d.outcome ? <DetailRow label="Decided" value={`${d.outcome} by ${d.by ?? '—'}${d.at ? `, ${fmtDateTime(d.at)}` : ''}`} /> : null}
          {o.note && o.note !== d.evidence ? <DetailRow label="Note" value={o.note} /> : null}
        </dl>
      </TechnicalDetail>

    </div>
    </Skeletonizer>
    </OrderActionsProvider>
  )
}

const orderPage = (id: string) => `/admin/renewal-orders/${id}`

const SOURCE_BUTTON: Record<RenewalOrderRow['source'], string> = {
  Ontraport: 'Open sale',
  Wizard: 'Open payment',
  Manual: 'Open payment',
}

function headline(o: RenewalOrderRow, who: string): string {
  switch (o.stage) {
    case 'Waiting': return o.reason === 'NotOfferedNow'
      ? `${who} paid; ASIC isn’t offering it for renewal yet`
      : `${who} paid early; it renews when ASIC opens the window`
    case 'Due': return `${who} paid; the next run renews it`
    case 'Renewing': return `${who} paid; renewing now`
    case 'Renewed': return `${who} paid, and it’s renewed`
    case 'Closed': return `${who} paid; ${inSentence(orderStatus(o.stage, o.reason).label)}`
    default: return `${who} paid, and it isn’t renewed: ${inSentence(orderStatus(o.stage, o.reason).label)}`
  }
}

/** What to do now, in a sentence, over the renewal's actions (see actionsFor: the usual fix first). */
function nextStep(o: RenewalOrderRow, d: RenewalOrderDecision, hint: string | undefined, actions: ReactNode): ReactNode {
  const note = o.note ? <span title={o.note}>{o.note.length > 160 ? o.note.slice(0, 159) + '…' : o.note}</span> : null
  switch (o.stage) {
    case 'NeedsPerson':
      return <NextStep meaning="action" title={NEEDS[o.reason ?? ''] ?? hint ?? 'This renewal needs you.'} actions={actions}>{note}</NextStep>
    case 'Renewing':
      return <NextStep meaning="working" title="Nothing to do; Renewtron is renewing it." actions={actions}>{o.nextActionAt ? <>Next try <When at={o.nextActionAt} />.{o.note ? ` ${o.note}` : ''}</> : 'This page updates by itself.'}</NextStep>
    case 'Due':
      return <NextStep meaning="working" title="Nothing to do yet." actions={actions}>The next processing run (07:00) renews it.</NextStep>
    case 'Waiting':
      return <NextStep meaning="waiting" title="Nothing to do; Renewtron looks after it." actions={actions}>
        {o.reason === 'NotOfferedNow' ? note : o.nextActionAt ? <>ASIC opens the window <When at={o.nextActionAt} />.</> : null}
      </NextStep>
    case 'Renewed':
      return <NextStep meaning="done" title="Nothing to do." actions={actions}>{o.renewedAt ? <>Renewed <When at={o.renewedAt} />.</> : 'Renewed.'}</NextStep>
    default:
      return <NextStep meaning="quiet" title={o.reason === 'RenewedElsewhere' ? 'Nothing to do. The payment is kept.' : 'Nothing to do.'} actions={actions}>
        {d.reference ? <>Reference {d.reference}. </> : null}{d.by ? <>Decided by {d.by}{d.at ? <> <When at={d.at} /></> : null}.</> : hint}
      </NextStep>
  }
}

/** For a renewal that needs a person: what to do, in the order of its buttons. */
const NEEDS: Record<string, string> = {
  NoDueDate: 'Find the due date at ASIC and set it; Renewtron takes it from there.',
  TooLate: 'Too late to renew online. Check ASIC: renewed elsewhere, register it again, or refund.',
  Lapsed: 'ASIC cancelled the name. Offer to register it again, or refund.',
  BelowMinimumFee: 'Paid under the fee. Renew it by hand and mark it renewed elsewhere, or refund.',
  Ineligible: 'Ontraport says not to renew it. Refund, or record that the client cancelled.',
  NameNotFound: 'ASIC doesn’t list this name under the ABN. Correct the name, or check ASIC.',
  NameMismatch: 'ASIC spells the name differently. Correct it and it goes back in the queue.',
  CardDeclined: 'ASIC declined the card. Try again.',
  AttemptsExhausted: 'Renewtron couldn’t get through to ASIC. Try again.',
  InProgressAtAsic: 'ASIC has another renewal open for this ABN. Try again once it clears.',
  VerifyPaymentAtAsic: 'ASIC may already hold the money. Check ASIC before anything else.',
  AsicRefused: 'ASIC refused the details. Check ASIC, then decide.',
}


/** What ASIC (or a person) said that the order's outcome rests on. */
function AsicSaid({ d }: { d: RenewalOrderDecision }) {
  return (
    <Card className="mt-6 gap-0 py-0">
      <CardHeader className="border-b py-4"><CardTitle>{d.evidenceSource === 'Staff' ? 'Why' : 'What ASIC said'}</CardTitle></CardHeader>
      <CardContent className="space-y-1 px-4 py-4 text-sm sm:px-6">
        {d.evidence ? <p>{d.evidence}</p> : <p className="text-muted-foreground">Nothing recorded yet.</p>}
        <p className="text-caption text-muted-foreground">
          {d.evidenceSource ? <>From {EVIDENCE_FROM[d.evidenceSource]}{d.evidenceAt ? <>, <When at={d.evidenceAt} /></> : null}. </> : null}
          {d.asicCheckedAt ? <>Renewtron last asked ASIC <When at={d.asicCheckedAt} />.</> : null}
        </p>
      </CardContent>
    </Card>
  )
}



function AttemptRow({ a }: { a: RenewalOrderAttempt }) {
  return (
    <Link to={`/admin/renewals/${a.id}`} className="flex flex-wrap items-center justify-between gap-3 border-t px-4 py-3 first:border-t-0 hover:bg-muted/50 sm:px-6">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
          <Status className="shrink-0" entry={renewalStatus(a)} />
          <span className="text-muted-foreground">started <When at={a.initiatedAt} /> · {a.attemptCount || 1} run{a.attemptCount === 1 ? '' : 's'} · pays by {a.paysBy}</span>
          {a.current ? <span className="text-caption text-muted-foreground">(current)</span> : null}
        </div>
        {a.errorMessage ? <div className="mt-0.5 truncate text-caption text-muted-foreground" title={a.errorMessage}>{a.failedAtStep ? `${a.failedAtStep}: ` : ''}{a.errorMessage}</div> : null}
      </div>
    </Link>
  )
}
