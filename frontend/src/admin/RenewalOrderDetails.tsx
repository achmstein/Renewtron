import { type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Loader2, RefreshCw } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type RenewalOrderAttempt, type RenewalOrderRow } from '../api/client'
import { Status, inSentence, orderStatus, renewalStatus } from './_status'
import { DueIn, When } from './_rows'
import { NextStep, RecordCrumbs, RecordHeader, TechnicalDetail, DetailRow } from './_record'
import { fmtDateTime, fmtMoney2, formatAbn } from './_utils'
import { Skeletonizer } from '@/components/skeletonizer'
import { sample } from '@/components/skeleton-samples'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

/**
 * One paid renewal: the one status that matters, the next step, what paid for it, and every
 * ASIC run for it (newest first). Sale and renewal pages are the detail behind it.
 */
// The page drawn as a skeleton while the renewal loads.
const SAMPLE_ORDER: Awaited<ReturnType<typeof api.admin.renewalOrder>> = {
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
}

export default function RenewalOrderDetails() {
  const { id } = useParams()
  const queryClient = useQueryClient()
  const { data: loaded, isPending, isError } = useQuery({
    queryKey: ['admin-renewal-order', id],
    queryFn: () => api.admin.renewalOrder(id!),
    enabled: !!id,
    refetchInterval: (q) => (q.state.data?.order.stage === 'Renewing' ? 5000 : false),
  })
  const data = loaded ?? (isPending ? SAMPLE_ORDER : undefined)
  const retry = useMutation({
    mutationFn: () => api.admin.retryRenewalOrder(id!),
    onSettled: () => {
      for (const key of ['admin-renewal-order', 'admin-renewal-orders', 'admin-today', 'admin-sidebar-counts'])
        void queryClient.invalidateQueries({ queryKey: [key] })
    },
  })
  const tryAgain = () => void sileo.promise(retry.mutateAsync(), {
    loading: { title: 'Trying again…' },
    success: (r) => ({ title: r.message }),
    error: (e) => ({ title: 'Not tried again', description: e instanceof Error ? e.message : undefined }),
  }).catch(() => {})

  if (isError || !data) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 text-center sm:px-6 lg:px-8">
        <h3 className="text-heading">Renewal not found</h3>
        <Button asChild variant="ghost" size="sm" className="mt-4"><Link to="/admin/renewal-orders">Back to paid renewals</Link></Button>
      </div>
    )
  }

  const o = data.order
  const status = orderStatus(o.stage, o.reason)
  const who = data.client?.fullName || o.clientName || o.payerName || 'The client'

  return (
    <Skeletonizer loading={isPending}>
    <div className="mx-auto max-w-5xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <RecordCrumbs client={data.client} businessName={o.businessName}
        list={{ to: '/admin/renewal-orders', label: 'Paid renewals' }} here={`Paid ${new Date(o.paidAt).toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })}`} />

      <RecordHeader
        title={headline(o, who)}
        sub={<>{o.businessName} · ABN {formatAbn(o.abn)}{o.dueDate ? <> · due <DueIn at={o.dueDate} /></> : ' · no due date'} · {fmtMoney2(o.amountPaid)} for {o.years} year{o.years === 1 ? '' : 's'}</>}
        status={status}
      >
        {nextStep(o, status.hint, o.canRetry ? (
          <Button size="sm" onClick={tryAgain} disabled={retry.isPending}>
            {retry.isPending ? <Loader2 className="animate-spin" /> : <RefreshCw />}Try again
          </Button>
        ) : null, <Button asChild size="sm" variant="outline"><Link to={data.source.to}>{SOURCE_BUTTON[o.source]}</Link></Button>)}
      </RecordHeader>

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
          {o.note ? <DetailRow label="Note" value={o.note} /> : null}
        </dl>
      </TechnicalDetail>
    </div>
    </Skeletonizer>
  )
}

const SOURCE_BUTTON: Record<RenewalOrderRow['source'], string> = {
  Ontraport: 'Open sale',
  Bulk: 'Open bulk upload',
  Wizard: 'Open renewal',
  Manual: 'Open renewal',
}

function headline(o: RenewalOrderRow, who: string): string {
  switch (o.stage) {
    case 'Waiting': return `${who} paid early; it renews when ASIC opens the window`
    case 'Due': return `${who} paid; the next run renews it`
    case 'Renewing': return `${who} paid; renewing now`
    case 'Renewed': return `${who} paid, and it’s renewed`
    case 'Closed': return `${who} paid; ${inSentence(orderStatus(o.stage, o.reason).label)}`
    default: return `${who} paid, and it isn’t renewed: ${inSentence(orderStatus(o.stage, o.reason).label)}`
  }
}

function nextStep(o: RenewalOrderRow, hint: string | undefined, retryButton: ReactNode, sourceButton: ReactNode): ReactNode {
  switch (o.stage) {
    case 'NeedsPerson':
      return <NextStep meaning="action" title={hint ?? 'This renewal needs you.'} actions={<>{retryButton}{sourceButton}</>}>{o.note ? <span title={o.note}>{o.note.length > 140 ? o.note.slice(0, 139) + '…' : o.note}</span> : null}</NextStep>
    case 'Renewing':
      return <NextStep meaning="working" title="Nothing to do; Renewtron is renewing it." actions={sourceButton}>{o.nextActionAt ? <>Next try <When at={o.nextActionAt} />.</> : 'This page updates by itself.'}</NextStep>
    case 'Due':
      return <NextStep meaning="working" title="Nothing to do yet." actions={sourceButton}>The next processing run (07:00) renews it.</NextStep>
    case 'Waiting':
      return <NextStep meaning="waiting" title="Nothing to do; it renews on its own." actions={sourceButton}>{o.nextActionAt ? <>ASIC opens the window <When at={o.nextActionAt} />.</> : null}</NextStep>
    case 'Renewed':
      return <NextStep meaning="done" title="Nothing to do." actions={sourceButton}>{o.renewedAt ? <>Renewed <When at={o.renewedAt} />.</> : 'Renewed.'}</NextStep>
    default:
      return <NextStep meaning="quiet" title="Nothing to do." actions={sourceButton}>{o.note ?? hint}</NextStep>
  }
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

