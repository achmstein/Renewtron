import { type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { Status, renewalStatus, statusOf, type StatusEntry } from './_status'
import { When } from './_rows'
import { NextStep, RecordCrumbs, RecordHeader, StoryStrip, TechnicalDetail, type StoryStep, DetailRow } from './_record'
import { fmtDateTime, fmtMoney2, formatAbn } from './_utils'
import { Skeletonizer } from '@/components/skeletonizer'
import { sample } from '@/components/skeleton-samples'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

type Detail = Awaited<ReturnType<typeof api.admin.search>>

const shortDate = (s: string) => new Date(s).toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })

/** Who ran the search, in words. */
const BY: Record<string, string> = { Customer: 'A visitor', Admin: 'Staff', System: 'Renewtron' }

// The page drawn as a skeleton while the search loads.
const SAMPLE_SEARCH: Detail = {
  id: sample.id(0),
  client: { id: sample.id(1), fullName: sample.name(1) },
  abn: sample.abn(1),
  searchedAt: sample.daysAgo(1),
  success: true,
  resultsCount: 3,
  initiatedBy: 'Customer',
  lead: { id: sample.id(2), fullName: sample.name(1), email: sample.email(1), mobileNumber: sample.mobile(), outcome: 'Converted' },
  results: [0, 1, 2].map((i) => ({
    id: sample.id(10 + i),
    businessName: sample.business(i + 1),
    accountNumber: '612 345 678',
    registrationDate: sample.daysAgo(700 + i * 300),
    renewalRequest: i === 0 ? { id: sample.id(20), status: 'Completed', initiatedAt: sample.daysAgo(1), renewalYears: 3, amount: 299 } : null,
  })),
  funnel: { hasLead: true, anyPaid: true, anyInflight: false, anyFailed: false },
}

export default function SearchDetails() {
  const { id } = useParams()
  const { data: loaded, isPending, isError } = useQuery({
    queryKey: ['admin-search', id],
    queryFn: () => api.admin.search(id!),
    enabled: !!id,
  })
  const data = loaded ?? (isPending ? SAMPLE_SEARCH : undefined)


  if (isError || !data) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 text-center sm:px-6 lg:px-8">
        <h3 className="text-heading">Search not found</h3>
        <Button asChild variant="ghost" size="sm" className="mt-4"><Link to="/admin/searches">Back to searches</Link></Button>
      </div>
    )
  }

  const status: StatusEntry = data.success
    ? { label: data.resultsCount ? 'Found names' : 'No names', meaning: data.resultsCount ? 'done' : 'quiet' }
    : { label: 'Search failed', meaning: 'quiet', hint: data.lead ? 'ASIC didn’t answer this lookup; the wizard carried on (see the wizard visit).' : 'ASIC didn’t answer; the visitor saw an error.' }
  const by = BY[data.initiatedBy] ?? data.initiatedBy

  return (
    <Skeletonizer loading={isPending}>
    <div className="mx-auto max-w-5xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <RecordCrumbs client={data.client} list={{ to: '/admin/searches', label: 'Searches' }} here={`ASIC search of ${shortDate(data.searchedAt)}`} />

      <RecordHeader
        title={!data.success
          ? `${by} looked up ABN ${formatAbn(data.abn)}; ASIC didn’t answer`
          : `${by} found ${data.resultsCount} business name${data.resultsCount === 1 ? '' : 's'} on ABN ${formatAbn(data.abn)}`}
        sub={<><When at={data.searchedAt} />{data.lead ? <> · went on to the wizard as {data.lead.fullName}</> : ' · no wizard visit followed'}</>}
        status={status}
      >
        {nextStep(data)}
        <StoryStrip steps={story(data)} />
      </RecordHeader>

      <Card className="mt-6 gap-4">
        <CardHeader><CardTitle>What ASIC returned</CardTitle></CardHeader>
        <CardContent>
        {data.results.length === 0 ? (
          <p className="text-sm text-muted-foreground">{data.success ? 'No business names on this ABN.' : 'Nothing: the search failed.'}</p>
        ) : (
          <div className="divide-y">
            {data.results.map((r) => (
              <div key={r.id} className="flex items-center justify-between gap-3 py-2.5">
                <div className="min-w-0">
                  <div className="truncate text-sm font-medium">{r.businessName}</div>
                  <div className="text-caption text-muted-foreground">
                    {r.renewalRequest
                      ? <>{r.renewalRequest.renewalYears}-year renewal, {fmtMoney2(r.renewalRequest.amount)}, <When at={r.renewalRequest.initiatedAt} /></>
                      : 'No renewal started'}
                  </div>
                </div>
                {r.renewalRequest
                  ? <Link to={`/admin/renewals/${r.renewalRequest.id}`} className="shrink-0"><Status entry={renewalStatus(r.renewalRequest)} /></Link>
                  : null}
              </div>
            ))}
          </div>
        )}
        </CardContent>
      </Card>

      <TechnicalDetail>
        <dl className="grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-2">
          <DetailRow label="Search ID" value={<span className="break-all">{data.id}</span>} mono />
          <DetailRow label="Searched" value={fmtDateTime(data.searchedAt)} />
          <DetailRow label="Started by" value={data.initiatedBy} />
          <DetailRow label="Result" value={`${data.success ? 'success' : 'failed'} · ${data.resultsCount} result(s)`} />
          <DetailRow label="IP address" value={data.ipAddress ?? '—'} mono />
          <DetailRow label="Session ID" value={<span className="break-all">{data.sessionId ?? '—'}</span>} mono />
          <DetailRow label="User agent" value={<span className="break-all text-caption">{data.userAgent ?? '—'}</span>} />
          {data.results.map((r) => <DetailRow key={r.id} label={r.businessName} value={`acct ${r.accountNumber} · reg ${r.registrationDate}`} />)}
        </dl>
        {data.errorMessage ? (
          <div className="mt-4">
            <div className="text-caption text-muted-foreground">Error</div>
            <pre className="mt-1 whitespace-pre-wrap break-words rounded-md border bg-muted/50 p-3 text-caption">{data.errorMessage}</pre>
          </div>
        ) : null}
      </TechnicalDetail>
    </div>
    </Skeletonizer>
  )
}

function nextStep(d: Detail): ReactNode {
  const renewals = d.results.map((r) => r.renewalRequest).filter((r): r is NonNullable<typeof r> => !!r)
  const stuck = renewals.find((r) => renewalStatus(r).meaning === 'action')
  if (stuck) {
    return (
      <NextStep meaning="action" title="A renewal from this search needs a person."
        actions={<Button asChild size="sm"><Link to={`/admin/renewals/${stuck.id}`}>Open renewal</Link></Button>}>
        {renewalStatus(stuck).label}, started <When at={stuck.initiatedAt} />.
      </NextStep>
    )
  }
  const openClient = d.client ? <Button asChild size="sm" variant="outline"><Link to={`/admin/clients/${d.client.id}`}>Open client</Link></Button> : null
  if (!d.success && d.lead) {
    return (
      <NextStep meaning="quiet" title="This lookup failed; the wizard visit carried on." actions={<Button asChild size="sm" variant="outline"><Link to={`/admin/leads/${d.lead.id}`}>Open wizard visit</Link></Button>}>
        ASIC didn’t answer this search, so the wizard used our copy of the register and checked with ASIC again. {d.lead.fullName}: {statusOf('lead', d.lead.outcome).label.toLowerCase()}.
      </NextStep>
    )
  }
  if (!d.success) return <NextStep meaning="quiet" title="Nothing to do." actions={openClient}>Searches fail when ASIC is slow or down; the visitor can try again.</NextStep>
  if (renewals.some((r) => r.status === 'Completed')) return <NextStep meaning="done" title="Nothing to do." actions={openClient}>A name from this search was renewed.</NextStep>
  if (d.lead) {
    return (
      <NextStep meaning="quiet" title="Follow the wizard visit." actions={<Button asChild size="sm" variant="outline"><Link to={`/admin/leads/${d.lead.id}`}>Open wizard visit</Link></Button>}>
        {d.lead.fullName}: {statusOf('lead', d.lead.outcome).label.toLowerCase()}.
      </NextStep>
    )
  }
  return <NextStep meaning="quiet" title="Nothing to do." actions={openClient}>They searched and left without giving their details.</NextStep>
}

/** Search → wizard visit → renewal(s). */
function story(d: Detail): StoryStep[] {
  const steps: StoryStep[] = [
    { label: 'ASIC search', meaning: d.success ? 'done' : 'quiet', at: d.searchedAt, note: d.success ? undefined : 'no answer', current: true },
  ]
  steps.push(d.lead
    ? { label: 'Wizard visit', meaning: 'done', note: statusOf('lead', d.lead.outcome).label.toLowerCase(), to: `/admin/leads/${d.lead.id}` }
    : { label: 'Wizard visit', meaning: 'quiet', note: 'didn’t continue' })
  const renewals = d.results.filter((r) => r.renewalRequest)
  if (renewals.length === 0) {
    steps.push({ label: 'Renewal', meaning: 'quiet', note: 'none' })
  } else {
    for (const r of renewals.slice(0, 3)) {
      const s = renewalStatus(r.renewalRequest!)
      steps.push({ label: r.businessName, meaning: s.meaning, note: s.label.toLowerCase(), to: `/admin/renewals/${r.renewalRequest!.id}` })
    }
  }
  return steps
}

