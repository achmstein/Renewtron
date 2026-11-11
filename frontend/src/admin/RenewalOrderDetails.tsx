import { useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Copy, Loader2, PenLine, RefreshCw, RotateCcw, SearchCheck } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type RenewalOrderAttempt, type RenewalOrderDecision, type RenewalOrderRow } from '../api/client'
import { Status, inSentence, orderStatus, renewalStatus } from './_status'
import { DueIn, When } from './_rows'
import { NextStep, RecordCrumbs, RecordHeader, TechnicalDetail, DetailRow } from './_record'
import { JourneyTimeline } from './_journey'
import { fmtDateTime, fmtMoney2, formatAbn } from './_utils'
import { Skeletonizer } from '@/components/skeletonizer'
import { sample } from '@/components/skeleton-samples'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
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

/** The outcomes a person records, with what the dialog asks for. */
const OUTCOMES: Record<string, { label: string; ask: string; reference?: string }> = {
  RenewedElsewhere: { label: 'Renewed elsewhere', ask: 'What shows it was renewed (what ASIC showed, or what the client said)? The payment is kept.' },
  Refunded: { label: 'Refunded', ask: 'Why it was refunded.', reference: 'Stripe or Ontraport refund reference' },
  RegisterAgain: { label: 'Register it again', ask: 'What the client was told. The payment is kept towards the new registration.' },
  ClientCancelling: { label: 'Client cancelled', ask: 'What the client asked for.' },
  Disputed: { label: 'Disputed', ask: 'What the dispute is about.', reference: 'Stripe dispute reference' },
  Duplicate: { label: 'Duplicate', ask: 'Which payment this duplicates.' },
}

const EVIDENCE_FROM: Record<NonNullable<RenewalOrderDecision['evidenceSource']>, string> = {
  AsicRegister: 'ASIC’s register file',
  AsicRenewalPage: 'ASIC’s renewal page',
  Staff: 'A person',
  Ontraport: 'Ontraport',
}

export default function RenewalOrderDetails() {
  const { id } = useParams()
  const queryClient = useQueryClient()
  const [deciding, setDeciding] = useState<string | null>(null)
  const [fixingName, setFixingName] = useState(false)
  const { data: loaded, isPending, isError } = useQuery({
    queryKey: ['admin-renewal-order', id],
    queryFn: () => api.admin.renewalOrder(id!),
    enabled: !!id,
    refetchInterval: (q) => (q.state.data?.order.stage === 'Renewing' ? 5000 : false),
  })
  const data = loaded ?? (isPending ? SAMPLE_ORDER : undefined)
  const refresh = () => {
    for (const key of ['admin-renewal-order', 'admin-renewal-orders', 'admin-today', 'admin-sidebar-counts'])
      void queryClient.invalidateQueries({ queryKey: [key] })
  }
  const act = useMutation({ mutationFn: (fn: () => Promise<{ message: string }>) => fn(), onSettled: refresh })
  const run = (fn: () => Promise<{ message: string }>, loading: string, failed: string) => void sileo.promise(act.mutateAsync(fn), {
    loading: { title: loading },
    success: (r) => ({ title: r.message }),
    error: (e) => ({ title: failed, description: e instanceof Error ? e.message : undefined }),
  }).catch(() => {})
  const check = useMutation({ mutationFn: () => api.admin.checkRenewalOrderAtAsic(id!), onSettled: refresh })
  const checkAsic = () => void sileo.promise(check.mutateAsync(), {
    loading: { title: 'Asking ASIC…' },
    success: (r) => ({ title: r.outcome ? `ASIC check: ${inSentence(orderStatus('NeedsPerson', r.outcome).label)}` : 'ASIC check done', description: r.evidence }),
    error: (e) => ({ title: 'Couldn’t check ASIC', description: e instanceof Error ? e.message : undefined }),
  }).catch(() => {})

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
  const busy = act.isPending || check.isPending
  const open = o.stage !== 'Renewed' && o.stage !== 'Closed' && o.stage !== 'Renewing'

  const buttons = {
    retry: o.canRetry ? (
      <Button size="sm" onClick={() => run(() => api.admin.retryRenewalOrder(o.id), 'Trying again…', 'Not tried again')} disabled={busy}>
        {act.isPending ? <Loader2 className="animate-spin" /> : <RefreshCw />}Try again
      </Button>
    ) : null,
    check: open ? (
      <Button size="sm" variant="outline" onClick={checkAsic} disabled={busy}>
        {check.isPending ? <Loader2 className="animate-spin" /> : <SearchCheck />}Check ASIC now
      </Button>
    ) : null,
    fixName: o.reason === 'NameMismatch' || o.reason === 'NameNotFound'
      ? (data.canFixName ? <Button size="sm" onClick={() => setFixingName(true)} disabled={busy}><PenLine />Correct the name</Button> : null)
      : null,
    elsewhere: open ? <Button size="sm" variant="outline" onClick={() => setDeciding('RenewedElsewhere')} disabled={busy}>Renewed elsewhere</Button> : null,
    lapsed: o.reason === 'Lapsed' ? (
      <>
        <Button size="sm" onClick={() => setDeciding('Refunded')} disabled={busy}>Refunded</Button>
        <Button size="sm" variant="outline" onClick={() => setDeciding('RegisterAgain')} disabled={busy}>Register it again</Button>
      </>
    ) : null,
    reopen: d.outcome ? (
      <Button size="sm" variant="ghost" onClick={() => run(() => api.admin.reopenRenewalOrder(o.id), 'Reopening…', 'Not reopened')} disabled={busy}>
        <RotateCcw />Undo
      </Button>
    ) : null,
    source: <Button asChild size="sm" variant="outline"><Link to={data.source.to}>{SOURCE_BUTTON[o.source]}</Link></Button>,
  }

  return (
    <Skeletonizer loading={isPending}>
    <div className={pageClass()}>
      <RecordCrumbs client={data.client} businessName={o.businessName}
        list={{ to: '/admin/renewal-orders', label: 'Renewals' }} here={`Paid ${new Date(o.paidAt).toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })}`} />

      <RecordHeader
        title={headline(o, who)}
        sub={<>{o.businessName} · ABN {formatAbn(o.abn)}{o.dueDate ? <> · due <DueIn at={o.dueDate} /></> : ' · no due date'} · {fmtMoney2(o.amountPaid)} for {o.years} year{o.years === 1 ? '' : 's'}</>}
        status={status}
      >
        {nextStep(o, d, status.hint, buttons)}
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

      <DecideDialog orderId={o.id} outcome={deciding} onClose={() => setDeciding(null)} onDone={refresh} />
      <FixNameDialog orderId={o.id} current={o.businessName} open={fixingName} onClose={() => setFixingName(false)} onDone={refresh} />
    </div>
    </Skeletonizer>
  )
}

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

type Buttons = Record<'retry' | 'check' | 'fixName' | 'elsewhere' | 'lapsed' | 'reopen' | 'source', ReactNode>

function nextStep(o: RenewalOrderRow, d: RenewalOrderDecision, hint: string | undefined, b: Buttons): ReactNode {
  const note = o.note ? <span title={o.note}>{o.note.length > 160 ? o.note.slice(0, 159) + '…' : o.note}</span> : null
  switch (o.stage) {
    case 'NeedsPerson':
      if (o.reason === 'Lapsed')
        return <NextStep meaning="action" title="Decide: refund the client, or offer to register the name again." actions={<>{b.lapsed}{b.check}{b.reopen}</>}>{note}</NextStep>
      if (o.reason === 'NameMismatch' || o.reason === 'NameNotFound')
        return <NextStep meaning="action" title={hint ?? 'Check the name at ASIC.'} actions={<>{b.fixName}{b.check}{b.elsewhere}{b.reopen}</>}>{note}</NextStep>
      return <NextStep meaning="action" title={hint ?? 'This renewal needs you.'} actions={<>{b.retry}{b.check}{b.elsewhere}{b.source}</>}>{note}</NextStep>
    case 'Renewing':
      return <NextStep meaning="working" title="Nothing to do; Renewtron is renewing it." actions={b.source}>{o.nextActionAt ? <>Next try <When at={o.nextActionAt} />.{o.note ? ` ${o.note}` : ''}</> : 'This page updates by itself.'}</NextStep>
    case 'Due':
      return <NextStep meaning="working" title="Nothing to do yet." actions={<>{b.check}{b.source}</>}>The next processing run (07:00) renews it.</NextStep>
    case 'Waiting':
      return <NextStep meaning="waiting" title="Nothing to do; Renewtron looks after it." actions={<>{b.check}{b.elsewhere}{b.reopen}{b.source}</>}>
        {o.reason === 'NotOfferedNow' ? note : o.nextActionAt ? <>ASIC opens the window <When at={o.nextActionAt} />.</> : null}
      </NextStep>
    case 'Renewed':
      return <NextStep meaning="done" title="Nothing to do." actions={b.source}>{o.renewedAt ? <>Renewed <When at={o.renewedAt} />.</> : 'Renewed.'}</NextStep>
    default:
      return <NextStep meaning="quiet" title={o.reason === 'RenewedElsewhere' ? 'Nothing to do. The payment is kept.' : 'Nothing to do.'} actions={<>{b.reopen}{b.source}</>}>
        {d.reference ? <>Reference {d.reference}. </> : null}{d.by ? <>Decided by {d.by}{d.at ? <> <When at={d.at} /></> : null}.</> : hint}
      </NextStep>
  }
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

function DecideDialog({ orderId, outcome, onClose, onDone }: { orderId: string; outcome: string | null; onClose: () => void; onDone: () => void }) {
  const [note, setNote] = useState('')
  const [reference, setReference] = useState('')
  const save = useMutation({ mutationFn: () => api.admin.setRenewalOrderOutcome(orderId, { outcome: outcome!, note, reference }) })
  const spec = outcome ? OUTCOMES[outcome] : null
  const registerLink = `${window.location.origin}/register`
  const close = () => { onClose(); setNote(''); setReference('') }
  const submit = () => void sileo.promise(save.mutateAsync(), {
    loading: { title: 'Saving…' },
    success: () => ({ title: `Marked ${spec ? inSentence(spec.label) : 'done'}` }),
    error: (e) => ({ title: 'Not saved', description: e instanceof Error ? e.message : undefined }),
  }).then(() => { onDone(); close() }).catch(() => {})

  return (
    <Dialog open={!!outcome} onOpenChange={(o) => { if (!o && !save.isPending) close() }}>
      <DialogContent className="sm:max-w-lg">
        {spec ? (
          <>
            <DialogHeader>
              <DialogTitle>{spec.label}</DialogTitle>
              <DialogDescription>{spec.ask}</DialogDescription>
            </DialogHeader>
            <DialogBody className="space-y-4">
              {outcome === 'RegisterAgain' ? (
                <div className="flex items-center gap-2 rounded-md border bg-muted/40 px-3 py-2 text-sm">
                  <span className="min-w-0 flex-1 truncate">Send the client: <span className="font-mono">{registerLink}</span></span>
                  <Button size="sm" variant="ghost" onClick={() => void navigator.clipboard.writeText(registerLink).then(() => sileo.success({ title: 'Link copied' }))}><Copy />Copy</Button>
                </div>
              ) : null}
              <div className="grid gap-1.5">
                <Label htmlFor="decide-note">Note</Label>
                <Textarea id="decide-note" value={note} onChange={(e) => setNote(e.target.value)} rows={3} autoFocus />
              </div>
              {spec.reference ? (
                <div className="grid gap-1.5">
                  <Label htmlFor="decide-ref">{spec.reference} (optional)</Label>
                  <Input id="decide-ref" value={reference} onChange={(e) => setReference(e.target.value)} className="font-mono" />
                </div>
              ) : null}
            </DialogBody>
            <DialogFooter>
              <Button variant="outline" onClick={close} disabled={save.isPending}>Cancel</Button>
              <Button onClick={submit} disabled={save.isPending || !note.trim()}>{save.isPending ? 'Saving…' : 'Save'}</Button>
            </DialogFooter>
          </>
        ) : null}
      </DialogContent>
    </Dialog>
  )
}

function FixNameDialog({ orderId, current, open, onClose, onDone }: { orderId: string; current: string; open: boolean; onClose: () => void; onDone: () => void }) {
  const [name, setName] = useState(current)
  const save = useMutation({ mutationFn: () => api.admin.fixRenewalOrderName(orderId, name) })
  const submit = () => void sileo.promise(save.mutateAsync(), {
    loading: { title: 'Saving…' },
    success: (r) => ({ title: r.message }),
    error: (e) => ({ title: 'Name not changed', description: e instanceof Error ? e.message : undefined }),
  }).then(() => { onDone(); onClose() }).catch(() => {})
  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o && !save.isPending) onClose() }}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Correct the business name</DialogTitle>
          <DialogDescription>Type it as ASIC shows it under the ABN. The sale goes back in the queue and renews on the next run.</DialogDescription>
        </DialogHeader>
        <DialogBody>
          <div className="grid gap-1.5">
            <Label htmlFor="fix-name">Business name</Label>
            <Input id="fix-name" value={name} onChange={(e) => setName(e.target.value)} autoFocus />
          </div>
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>Cancel</Button>
          <Button onClick={submit} disabled={save.isPending || !name.trim() || name.trim() === current}>{save.isPending ? 'Saving…' : 'Save and requeue'}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
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
