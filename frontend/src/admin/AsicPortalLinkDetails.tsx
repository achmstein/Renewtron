import { type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Loader2, RefreshCw } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type AsicPortalLinkDto } from '../api/client'
import { statusOf, type Meaning } from './_status'
import { When } from './_rows'
import { DetailRow, NextStep, RecordCrumbs, RecordHeader, StoryStrip, TechnicalDetail, type StoryStep } from './_record'
import { fmtDateTime, formatAbn } from './_utils'
import { Skeletonizer } from '@/components/skeletonizer'
import { sample } from '@/components/skeleton-samples'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { pageClass } from './_ui'

/*
 * One "link to our ASIC account" run (/admin/asic-portal/:id): link the name in ASIC Connect with
 * the client's key, then our business email, the holder's email and our representative only.
 */

const DECLARE_WORDS = {
  Holder: 'as the holder (“I am the registration or licence holder”)',
  OnBehalfOfHolder: 'on behalf of the holder (“submitting on behalf of the holder and authorised to”)',
} as const

/** A step's outcome in words and colour. */
function outcome(o: string | null, done: string): { meaning: Meaning; note: string } {
  switch (o) {
    case 'success': return { meaning: 'done', note: done }
    case 'already_exists': return { meaning: 'done', note: 'already on our account' }
    case 'no_action': return { meaning: 'done', note: 'nothing to change' }
    case 'no_address': return { meaning: 'done', note: 'no address to keep' }
    case 'pending': return { meaning: 'action', note: 'needs a person' }
    case 'failed': return { meaning: 'action', note: 'failed' }
    case 'unknown': return { meaning: 'action', note: 'unclear' }
    case null: case undefined: return { meaning: 'quiet', note: 'not yet' }
    default: return { meaning: 'action', note: o }
  }
}

// The page drawn as a skeleton while the run loads.
const SAMPLE_RUN: AsicPortalLinkDto = {
  id: sample.id(0), clientId: sample.id(1), clientName: sample.name(6), clientBusinessNameId: sample.id(2),
  businessName: sample.business(6), abn: sample.abn(6), dateOfBirth: '1988-06-21', dateOfBirthSource: 'Ontraport',
  ontraportContactId: '184522', declareAs: 'OnBehalfOfHolder', status: 'Done',
  linkOutcome: 'success', emailOutcome: 'success', emailReference: '2-98765432101', holderOutcome: 'success', holderType: 'Individual',
  holderReference: '2-98765432102', repsOutcome: 'no_action', repsRemoved: [], repsReferences: [],
  failedAtStep: null, error: null, ontraportResult: null, requestedBy: 'Admin', attemptCount: 1,
  createdAt: sample.daysAgo(2), startedAt: sample.daysAgo(2), finishedAt: sample.daysAgo(2),
}

export default function AsicPortalLinkDetails() {
  const { id } = useParams()
  const queryClient = useQueryClient()
  const { data: loaded, isPending, isError } = useQuery({
    queryKey: ['admin-asic-portal-link', id],
    queryFn: () => api.admin.asicPortalLink(id!),
    enabled: !!id,
    // Follow a run while it goes.
    refetchInterval: (q) => (q.state.data && ['Queued', 'Running'].includes(q.state.data.status) ? 4000 : false),
  })
  const r = loaded ?? (isPending ? SAMPLE_RUN : undefined)
  const retry = useMutation({
    mutationFn: () => api.admin.retryAsicPortalLink(id!),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: ['admin-asic-portal-link', id] })
      void queryClient.invalidateQueries({ queryKey: ['admin-client'] })
    },
  })

  if (isError || !r) return <div className={pageClass('py-12 text-center')}><h3 className="text-heading">Run not found</h3></div>

  const status = statusOf('asicPortal', r.status)
  const client = r.clientId ? { id: r.clientId, fullName: r.clientName ?? 'Client' } : null
  const openClient = r.clientId ? <Button asChild size="sm" variant="outline"><Link to={`/admin/clients/${r.clientId}`}>Open client</Link></Button> : null
  const runRetry = () => void sileo.promise(retry.mutateAsync(), {
    loading: { title: 'Queuing it again…' },
    success: () => ({ title: 'Queued; it carries on from the first step that isn’t done.' }),
    error: (e) => ({ title: 'Couldn’t retry it', description: e instanceof Error ? e.message : undefined }),
  }).catch(() => {})
  const retryButton = <Button size="sm" onClick={runRetry} disabled={retry.isPending}>{retry.isPending ? <Loader2 className="animate-spin" /> : <RefreshCw />}Retry</Button>

  let next: ReactNode
  switch (r.status) {
    case 'Queued':
    case 'Running':
      next = <NextStep meaning="working" title="Nothing to do; it’s going through ASIC Connect.">Started <When at={r.startedAt ?? r.createdAt} />. This page updates by itself.</NextStep>
      break
    case 'Done':
      next = <NextStep meaning="done" title="Nothing to do." actions={openClient}>Finished <When at={r.finishedAt ?? r.createdAt} />.</NextStep>
      break
    case 'WrongKey':
      next = <NextStep meaning="action" title="Ask the client for the right ASIC key." actions={<>{retryButton}{openClient}</>}>ASIC didn’t accept the key and date of birth. Once the key on the name is right, retry.</NextStep>
      break
    default:
      next = <NextStep meaning="action" title={r.status === 'NeedsLook' ? 'A step needs a person.' : 'It didn’t get through.'} actions={<>{retryButton}{openClient}</>}>
        <span className="whitespace-pre-wrap">{r.error ?? 'No note was left.'}</span>
      </NextStep>
  }

  const link = outcome(r.linkOutcome, 'linked')
  const email = outcome(r.emailOutcome, r.emailReference ? `ref ${r.emailReference}` : 'changed')
  const holder = outcome(r.holderOutcome, r.holderReference ? `ref ${r.holderReference}` : 'changed')
  const reps = outcome(r.repsOutcome, 'done')
  const steps: StoryStep[] = [
    { label: 'Linked', ...link, at: undefined },
    { label: 'Business email', ...email },
    { label: 'Holder email', ...holder },
    { label: 'Representatives', ...reps, note: r.repsRemoved.length ? `${r.repsRemoved.length} removed` : reps.note },
  ]

  return (
    <Skeletonizer loading={isPending}>
    <div className={pageClass()}>
      <RecordCrumbs client={client} businessName={r.businessName} list={{ to: '/admin/clients', label: 'Clients' }} here="Link to our ASIC account" />
      <RecordHeader
        title={`Link ${r.businessName} to our ASIC account`}
        sub={<>ABN {formatAbn(r.abn)} · declared {r.declareAs === 'Holder' ? 'as the holder' : 'on behalf of the holder'} · asked <When at={r.createdAt} />{r.requestedBy ? ` by ${r.requestedBy}` : ''}</>}
        status={status}
      >
        {next}
        <StoryStrip steps={steps} />
      </RecordHeader>

      <div className="mt-6">
        <Card className="gap-4">
          <CardHeader><CardTitle>What was done at ASIC</CardTitle></CardHeader>
          <CardContent>
            <dl className="space-y-3">
              <DetailRow label="Link" value={link.note} />
              <DetailRow label="Business email" value={email.note} />
              <DetailRow label="Holder" value={r.holderType ? `${r.holderType} · ${holder.note}` : holder.note} />
              <DetailRow label="Representatives removed" value={r.repsRemoved.length
                ? r.repsRemoved.map((n, i) => `${n}${r.repsReferences[i] ? ` (ref ${r.repsReferences[i]})` : ''}`).join(', ')
                : reps.note} />
              <DetailRow label="Declared" value={DECLARE_WORDS[r.declareAs]} />
              <DetailRow label="Ontraport" value={r.ontraportResult || (r.ontraportContactId ? 'not written yet' : 'no contact')} />
            </dl>
          </CardContent>
        </Card>
      </div>

      <TechnicalDetail>
        <dl className="grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-2">
          <DetailRow label="Run ID" value={<span className="break-all">{r.id}</span>} mono />
          <DetailRow label="Status" value={r.status} />
          <DetailRow label="Date of birth used" value={`${r.dateOfBirth} (from ${r.dateOfBirthSource.toLowerCase()})`} />
          <DetailRow label="Ontraport contact" value={r.ontraportContactId ?? '—'} mono />
          <DetailRow label="Attempts" value={String(r.attemptCount)} />
          <DetailRow label="Created" value={fmtDateTime(r.createdAt)} />
          <DetailRow label="Started" value={r.startedAt ? fmtDateTime(r.startedAt) : '—'} />
          <DetailRow label="Finished" value={r.finishedAt ? fmtDateTime(r.finishedAt) : '—'} />
          <DetailRow label="Stopped at" value={r.failedAtStep ?? '—'} />
        </dl>
        {r.error ? <pre className="mt-4 max-h-72 overflow-y-auto whitespace-pre-wrap break-words rounded-md border bg-muted/50 p-3 text-caption">{r.error}</pre> : null}
      </TechnicalDetail>
    </div>
    </Skeletonizer>
  )
}

