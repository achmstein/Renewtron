import { type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Copy, Loader2, RefreshCw } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type AsicKeyNotificationDto, type AsicKeyRequestDto } from '../api/client'
import { statusOf } from './_status'
import { When } from './_rows'
import { NextStep, RecordCrumbs, RecordHeader, StoryStrip, TechnicalDetail, type StoryStep } from './_record'
import { fmtDateTime, formatAbn } from './_utils'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'

/*
 * Record pages for the two ends of getting a client's ASIC key: the request we send ASIC
 * (/admin/asic-key-requests/:id) and the key letter ASIC emails back (/admin/asic-keys/:id).
 */

const shortDate = (s: string) => new Date(s).toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })

function useInvalidateKeys() {
  const queryClient = useQueryClient()
  return () => {
    for (const key of ['admin-asic-key', 'admin-asic-key-context', 'admin-asic-keys', 'admin-asic-key-request', 'admin-asic-key-requests', 'admin-today', 'admin-sidebar-counts']) {
      void queryClient.invalidateQueries({ queryKey: [key] })
    }
  }
}

function Loading() {
  return (
    <div className="mx-auto max-w-5xl px-4 py-12 text-center sm:px-6 lg:px-8">
      <Loader2 className="mx-auto size-6 animate-spin text-muted-foreground" />
    </div>
  )
}

function NotFound({ what, back }: { what: string; back: { to: string; label: string } }) {
  return (
    <div className="mx-auto max-w-5xl px-4 py-12 text-center sm:px-6 lg:px-8">
      <h3 className="text-heading">{what} not found</h3>
      <Button asChild variant="ghost" size="sm" className="mt-4"><Link to={back.to}>Back to {back.label}</Link></Button>
    </div>
  )
}

/* ─────────────────────────── Key letter ─────────────────────────── */

const LETTER_HEADLINES: Record<string, (k: AsicKeyNotificationDto) => string> = {
  Completed: (k) => `Key ${k.asicKey ?? ''} saved to Ontraport`,
  SavedToClient: (k) => `Key ${k.asicKey ?? ''} saved on the client`,
  Pending: () => 'Reading the key letter',
  LinkNotFound: () => 'The letter had no download link',
  DownloadFailed: () => 'Couldn’t download the letter',
  KeyNotFound: () => 'No key found in the letter',
  ContactNotFound: (k) => `Key ${k.asicKey ?? ''} read, but no Ontraport contact holds this name`,
  OntraportUpdateFailed: (k) => `Key ${k.asicKey ?? ''} read; writing it to Ontraport failed`,
}

export function KeyLetterDetails() {
  const { id } = useParams()
  const invalidate = useInvalidateKeys()
  const letter = useQuery({ queryKey: ['admin-asic-key', id], queryFn: () => api.admin.asicKey(id!), enabled: !!id })
  const context = useQuery({ queryKey: ['admin-asic-key-context', id], queryFn: () => api.admin.asicKeyContext(id!), enabled: !!id })
  const retry = useMutation({ mutationFn: () => api.admin.retryAsicKey(id!), onSettled: invalidate })

  if (letter.isPending) return <Loading />
  if (letter.isError || !letter.data) return <NotFound what="Key letter" back={{ to: '/admin/asic-keys', label: 'key letters' }} />
  const k = letter.data
  const ctx = context.data
  const status = statusOf('keyLetter', k.status)

  const runRetry = () => void sileo.promise(retry.mutateAsync(), {
    loading: { title: 'Reading the key letter again…' },
    success: (r) => r.status === 'Completed' || r.status === 'SavedToClient'
      ? { title: `Key ${r.asicKey ?? ''} saved` }
      : { title: `Still ${statusOf('keyLetter', r.status).label.toLowerCase()}`, description: r.errorMessage ?? undefined },
    error: (e) => ({ title: 'Retry failed', description: e instanceof Error ? e.message : undefined }),
  }).catch(() => {})
  const retryButton = (
    <Button size="sm" onClick={runRetry} disabled={retry.isPending}>
      {retry.isPending ? <Loader2 className="animate-spin" /> : <RefreshCw />}Read it again
    </Button>
  )
  const openClient = ctx?.client ? <Button asChild size="sm" variant="outline"><Link to={`/admin/clients/${ctx.client.id}`}>Open client</Link></Button> : null

  let next: ReactNode
  if (status.meaning === 'done') {
    next = <NextStep meaning="done" title="Nothing to do." actions={openClient}>Read <When at={k.processedAt ?? k.receivedAt} />{k.ontraportContactsUpdated ? `; written to ${k.ontraportContactsUpdated} Ontraport contact${k.ontraportContactsUpdated === 1 ? '' : 's'}` : ''}.</NextStep>
  } else if (k.status === 'Pending') {
    next = <NextStep meaning="working" title="Nothing to do yet.">The inbox scan reads it within 15 minutes.</NextStep>
  } else if (k.status === 'ContactNotFound') {
    next = (
      <NextStep meaning="action" title="Choose the Ontraport contact this key belongs to."
        actions={<><Button asChild size="sm"><Link to="/admin/asic-keys?view=to-sort">Choose contact</Link></Button>{retryButton}</>}>
        <span title="If the client has since been added to Ontraport, reading it again finds them.">The key is safe here.</span>
      </NextStep>
    )
  } else if (k.status === 'DownloadFailed') {
    next = (
      <NextStep meaning="action" title="Read it again, or ask ASIC for the key again."
        actions={<>{retryButton}{ctx?.sale ? <Button asChild size="sm" variant="outline"><Link to={`/admin/ontraport-sales/${ctx.sale.id}`}>Open sale</Link></Button> : openClient}</>}>
        ASIC keeps a letter’s download for 30 days; after that it’s gone and a new request is needed.
      </NextStep>
    )
  } else {
    next = <NextStep meaning="action" title="Read it again." actions={<>{retryButton}{openClient}</>}>{status.hint ?? k.errorMessage ?? ''}</NextStep>
  }

  const steps: StoryStep[] = []
  if (ctx?.sale) steps.push({ label: 'Ontraport sale', meaning: 'done', at: ctx.sale.syncedAt, to: `/admin/ontraport-sales/${ctx.sale.id}` })
  if (ctx?.keyRequest) {
    const s = statusOf('keyRequest', ctx.keyRequest.status)
    steps.push({ label: 'Key request', meaning: s.meaning, at: ctx.keyRequest.createdAt, to: `/admin/asic-key-requests/${ctx.keyRequest.id}` })
  }
  steps.push({ label: 'Key letter', meaning: k.asicKey ? 'done' : status.meaning, at: k.receivedAt, current: true })
  steps.push({ label: k.status === 'SavedToClient' ? 'Saved on client' : 'Saved to Ontraport', meaning: status.meaning === 'done' ? 'done' : status.meaning === 'working' ? 'quiet' : 'action', note: status.meaning === 'done' ? undefined : status.label.toLowerCase(), at: status.meaning === 'done' ? k.processedAt : undefined })

  return (
    <div className="mx-auto max-w-5xl px-4 py-8 sm:px-6 lg:px-8">
      <RecordCrumbs client={ctx?.client} businessName={k.businessName} list={{ to: '/admin/asic-keys', label: 'Key letters' }} here={`Key letter of ${shortDate(k.receivedAt)}`} />
      <RecordHeader
        title={(LETTER_HEADLINES[k.status] ?? (() => status.label))(k)}
        sub={<>{k.businessName ?? 'Business name not read'}{k.abn ? ` · ABN ${formatAbn(k.abn)}` : ''} · arrived <When at={k.receivedAt} /></>}
        status={status}
      >
        {next}
        <StoryStrip steps={steps} />
      </RecordHeader>

      {k.asicKey ? (
        <Card className="mt-6 gap-2">
          <CardHeader>
            <CardTitle>ASIC key</CardTitle>
            <CardAction>
              <Button size="sm" variant="outline" onClick={() => { void navigator.clipboard.writeText(k.asicKey ?? ''); sileo.success({ title: 'Key copied' }) }}><Copy />Copy</Button>
            </CardAction>
          </CardHeader>
          <CardContent><div className="font-mono text-2xl font-semibold">{k.asicKey}</div></CardContent>
        </Card>
      ) : null}

      <TechnicalDetail>
        <dl className="grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-2">
          <Row label="Letter ID" value={<span className="break-all">{k.id}</span>} mono />
          <Row label="Status" value={k.status} />
          <Row label="Subject" value={k.subject ?? '—'} />
          <Row label="From" value={k.from ?? '—'} />
          <Row label="Received" value={fmtDateTime(k.receivedAt)} />
          <Row label="Processed" value={k.processedAt ? fmtDateTime(k.processedAt) : '—'} />
          <Row label="Attempts" value={String(k.attemptCount)} />
          <Row label="Ontraport ids" value={k.ontraportContactIds ?? '—'} mono />
          <Row label="Download link" value={k.downloadUrl ? <a href={k.downloadUrl} target="_blank" rel="noreferrer" className="break-all text-link hover:underline">{k.downloadUrl}</a> : '—'} />
        </dl>
        {k.errorMessage || k.pdfTextExcerpt ? (
          <div className="mt-4 space-y-3">
            {k.errorMessage ? <Pre label="Error" text={k.errorMessage} /> : null}
            {k.pdfTextExcerpt ? <Pre label="Text read from the PDF" text={k.pdfTextExcerpt} /> : null}
          </div>
        ) : null}
      </TechnicalDetail>
    </div>
  )
}

/* ─────────────────────────── Key request ─────────────────────────── */

function requestHeadline(r: AsicKeyRequestDto): string {
  switch (r.status) {
    case 'Pending': return 'To send: waiting for someone to send it from the key tool'
    case 'Manual': return 'Being sent to ASIC by a person now'
    case 'Submitted': return `Sent to ASIC${r.asicReferenceNumber ? ` (ref ${r.asicReferenceNumber})` : ''}; waiting for the key email`
    case 'KeyReceived': return 'ASIC sent the key'
    case 'Failed': return 'Couldn’t be sent to ASIC'
    default: return statusOf('keyRequest', r.status).label
  }
}

export function KeyRequestDetails() {
  const { id } = useParams()
  const invalidate = useInvalidateKeys()
  const { data, isPending, isError } = useQuery({ queryKey: ['admin-asic-key-request', id], queryFn: () => api.admin.asicKeyRequest(id!), enabled: !!id })
  const requeue = useMutation({ mutationFn: () => api.admin.requeueAsicKeyRequest(id!), onSettled: invalidate })

  if (isPending) return <Loading />
  if (isError || !data) return <NotFound what="Key request" back={{ to: '/admin/asic-key-requests', label: 'key requests' }} />
  const r = data.request
  const status = statusOf('keyRequest', r.status)
  const openClient = data.client ? <Button asChild size="sm" variant="outline"><Link to={`/admin/clients/${data.client.id}`}>Open client</Link></Button> : null
  const names = [r.businessName, ...r.otherBusinessNames]

  const runRequeue = () => void sileo.promise(requeue.mutateAsync(), {
    loading: { title: 'Putting it back on the list…' },
    success: () => ({ title: 'Back on the key requests to send.' }),
    error: (e) => ({ title: 'Couldn’t requeue it', description: e instanceof Error ? e.message : undefined }),
  }).catch(() => {})

  let next: ReactNode
  switch (r.status) {
    case 'Pending':
      next = <NextStep meaning="action" title="Send it from the key tool on the office PC." actions={<><Button asChild size="sm"><Link to="/admin/asic-key-requests">Key requests</Link></Button>{openClient}</>}><span title="The tool fills in ASIC’s form; a person submits it and records the reference.">Waiting since <When at={r.createdAt} />.</span></NextStep>
      break
    case 'Manual':
      next = <NextStep meaning="working" title="Nothing to do; someone is on it.">They record ASIC’s reference number when it’s sent.</NextStep>
      break
    case 'Submitted':
      next = <NextStep meaning="waiting" title="Waiting for ASIC to email the key." actions={openClient}><span title="The inbox scan matches the key letter when it arrives.">Sent <When at={r.submittedAt ?? r.processedAt ?? r.createdAt} />.</span></NextStep>
      break
    case 'KeyReceived':
      next = <NextStep meaning="done" title="Nothing to do." actions={data.letter ? <Button asChild size="sm" variant="outline"><Link to={`/admin/asic-keys/${data.letter.id}`}>Open key letter</Link></Button> : openClient}>Key received <When at={r.keyReceivedAt ?? r.processedAt ?? r.createdAt} />.</NextStep>
      break
    default:
      next = (
        <NextStep meaning="action" title="Fix what stopped it, then put it back on the list."
          actions={<><Button size="sm" onClick={runRequeue} disabled={requeue.isPending}>{requeue.isPending ? <Loader2 className="animate-spin" /> : <RefreshCw />}Send again</Button>{openClient}</>}>
          {r.errorMessage ?? 'No note was left.'}
        </NextStep>
      )
  }

  const steps: StoryStep[] = []
  if (data.sale) steps.push({ label: 'Ontraport sale', meaning: 'done', at: data.sale.syncedAt, to: `/admin/ontraport-sales/${data.sale.id}` })
  steps.push({ label: 'Key request', meaning: 'done', at: r.createdAt, current: true })
  steps.push(r.submittedAt || r.status === 'Submitted' || r.status === 'KeyReceived'
    ? { label: 'Sent to ASIC', meaning: 'done', at: r.submittedAt ?? r.processedAt, note: r.asicReferenceNumber ? `ref ${r.asicReferenceNumber}` : undefined }
    : { label: 'Sent to ASIC', meaning: status.meaning === 'action' ? 'action' : 'quiet', note: status.label.toLowerCase() })
  steps.push(data.letter
    ? { label: 'Key letter', meaning: data.letter.hasKey ? 'done' : statusOf('keyLetter', data.letter.status).meaning, at: data.letter.receivedAt, to: `/admin/asic-keys/${data.letter.id}` }
    : { label: 'Key letter', meaning: 'quiet', note: 'not yet' })

  return (
    <div className="mx-auto max-w-5xl px-4 py-8 sm:px-6 lg:px-8">
      <RecordCrumbs client={data.client} businessName={r.businessName} list={{ to: '/admin/asic-key-requests', label: 'Key requests' }} here={`Key request of ${shortDate(r.createdAt)}`} />
      <RecordHeader
        title={requestHeadline(r)}
        sub={<>{names.length === 1 ? r.businessName : `${names.length} business names`} · ABN {formatAbn(r.abn)} · for {r.contactName} · <When at={r.createdAt} />{r.isTest ? ' · test' : ''}</>}
        status={status}
      >
        {next}
        <StoryStrip steps={steps} />
      </RecordHeader>

      <div className="mt-6">
        <Section title={`The key${names.length === 1 ? '' : 's'} for ${names.join(', ')}`}>
          <Row label="Contact" value={`${r.contactName} · ${r.email}`} />
          <Row label="Phone" value={r.phone ? `${r.phonePrefix} ${r.phoneNumber}` : '—'} />
          {r.question ? <Row label="Message" value={<span className="whitespace-pre-wrap">{r.question}</span>} /> : null}
        </Section>
      </div>

      <TechnicalDetail>
        <dl className="grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-2">
          <Row label="Request ID" value={<span className="break-all">{r.id}</span>} mono />
          <Row label="Status" value={`${r.status}${r.canAutoRetry ? '' : ' · no auto-retry'}`} />
          <Row label="Source" value={r.source} />
          <Row label="Attempts" value={String(r.attemptCount)} />
          <Row label="Created" value={fmtDateTime(r.createdAt)} />
          <Row label="Processed" value={r.processedAt ? fmtDateTime(r.processedAt) : '—'} />
          <Row label="Submitted" value={r.submittedAt ? fmtDateTime(r.submittedAt) : '—'} />
          <Row label="Key received" value={r.keyReceivedAt ? fmtDateTime(r.keyReceivedAt) : '—'} />
          <Row label="Ontraport contact" value={r.ontraportContactId ?? '—'} mono />
        </dl>
        {r.errorMessage ? <div className="mt-4"><Pre label="Note" text={r.errorMessage} /></div> : null}
      </TechnicalDetail>
    </div>
  )
}

function Pre({ label, text }: { label: string; text: string }) {
  return (
    <div>
      <div className="text-caption text-muted-foreground">{label}</div>
      <pre className="mt-1 max-h-72 overflow-y-auto whitespace-pre-wrap break-words rounded-md border bg-muted/50 p-3 text-caption">{text}</pre>
    </div>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Card className="gap-4">
      <CardHeader><CardTitle>{title}</CardTitle></CardHeader>
      <CardContent><dl className="space-y-3">{children}</dl></CardContent>
    </Card>
  )
}

function Row({ label, value, mono }: { label: string; value: ReactNode; mono?: boolean }) {
  return (
    <div className="grid grid-cols-3 items-baseline gap-3 lg:grid-cols-4">
      <dt className="truncate text-caption text-muted-foreground">{label}</dt>
      <dd className={cn('col-span-2 min-w-0 text-sm lg:col-span-3', mono && 'font-mono')}>{value}</dd>
    </div>
  )
}
