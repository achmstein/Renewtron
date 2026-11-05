import { type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Loader2 } from 'lucide-react'
import { api } from '../api/client'
import { KickerLabel } from './_ui'
import { Status, renewalStatus, statusOf, type StatusEntry } from './_status'
import { When } from './_rows'
import { NextStep, RecordCrumbs, RecordHeader, StoryStrip, TechnicalDetail, type StoryStep } from './_record'
import { fmtDateTime, fmtMoney2, formatAbn } from './_utils'
import { Button } from '@/components/ui/button'

type Detail = Awaited<ReturnType<typeof api.admin.search>>

const shortDate = (s: string) => new Date(s).toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })

/** Who ran the search, in words. */
const BY: Record<string, string> = { Customer: 'A visitor', Admin: 'Staff', System: 'Renewtron' }

export default function SearchDetails() {
  const { id } = useParams()
  const { data, isPending, isError } = useQuery({
    queryKey: ['admin-search', id],
    queryFn: () => api.admin.search(id!),
    enabled: !!id,
  })

  if (isPending) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 text-center sm:px-6 lg:px-8">
        <Loader2 className="mx-auto h-8 w-8 animate-spin text-brand-600" />
        <p className="mt-3 text-sm text-zinc-500">Loading…</p>
      </div>
    )
  }

  if (isError || !data) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 text-center sm:px-6 lg:px-8">
        <h3 className="text-base font-semibold text-zinc-900">Search not found</h3>
        <p className="mt-1 text-sm text-zinc-500">The search you’re looking for doesn’t exist.</p>
        <Link to="/admin/searches" className="mt-4 inline-flex items-center text-sm font-medium text-brand-700 hover:text-brand-800">← Back to searches</Link>
      </div>
    )
  }

  const status: StatusEntry = data.success
    ? { label: data.resultsCount ? 'Found names' : 'No names', meaning: data.resultsCount ? 'done' : 'quiet' }
    : { label: 'Search failed', meaning: 'quiet', hint: 'ASIC didn’t answer; the visitor saw an error.' }
  const by = BY[data.initiatedBy] ?? data.initiatedBy

  return (
    <div className="mx-auto max-w-5xl px-4 py-8 sm:px-6 lg:px-8">
      <RecordCrumbs client={data.client} list={{ to: '/admin/searches', label: 'Searches' }} here={`ASIC search of ${shortDate(data.searchedAt)}`} />

      <RecordHeader
        kicker={`ASIC search · ABN ${formatAbn(data.abn)}`}
        title={!data.success
          ? `${by} looked up ABN ${formatAbn(data.abn)}; ASIC didn’t answer`
          : `${by} found ${data.resultsCount} business name${data.resultsCount === 1 ? '' : 's'} on ABN ${formatAbn(data.abn)}`}
        sub={<><When at={data.searchedAt} />{data.lead ? <> · went on to the wizard as {data.lead.fullName}</> : ' · no wizard visit followed'}</>}
        status={status}
      >
        {nextStep(data)}
        <StoryStrip steps={story(data)} />
      </RecordHeader>

      <div className="mt-6 rounded-xl bg-white p-5 ring-1 ring-zinc-200 shadow-sm">
        <div className="mb-4">
          <KickerLabel>Names</KickerLabel>
          <h2 className="mt-0.5 text-base font-semibold tracking-tight text-zinc-900">What ASIC returned</h2>
        </div>
        {data.results.length === 0 ? (
          <p className="text-sm text-zinc-500">{data.success ? 'No business names on this ABN.' : 'Nothing: the search failed.'}</p>
        ) : (
          <div className="divide-y divide-zinc-100">
            {data.results.map((r) => (
              <div key={r.id} className="flex items-center justify-between gap-3 py-2.5">
                <div className="min-w-0">
                  <div className="truncate text-sm font-medium text-zinc-900">{r.businessName}</div>
                  <div className="text-xs text-zinc-500">
                    {r.renewalRequest
                      ? <>{r.renewalRequest.renewalYears}-year renewal, {fmtMoney2(r.renewalRequest.amount)}, <When at={r.renewalRequest.initiatedAt} /></>
                      : 'No renewal started'}
                  </div>
                </div>
                {r.renewalRequest
                  ? <Link to={`/admin/renewals/${r.renewalRequest.id}`}><Status entry={renewalStatus(r.renewalRequest)} /></Link>
                  : null}
              </div>
            ))}
          </div>
        )}
      </div>

      <TechnicalDetail>
        <dl className="grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-2">
          <Row label="Search ID" value={<span className="break-all">{data.id}</span>} />
          <Row label="Searched" value={fmtDateTime(data.searchedAt)} />
          <Row label="Started by" value={data.initiatedBy} />
          <Row label="Result" value={`${data.success ? 'success' : 'failed'} · ${data.resultsCount} result(s)`} />
          <Row label="IP address" value={data.ipAddress ?? '—'} />
          <Row label="Session ID" value={<span className="break-all">{data.sessionId ?? '—'}</span>} />
          <Row label="User agent" value={<span className="break-all text-xs">{data.userAgent ?? '—'}</span>} />
          {data.results.map((r) => <Row key={r.id} label={r.businessName} value={`acct ${r.accountNumber} · reg ${r.registrationDate}`} />)}
        </dl>
        {data.errorMessage ? (
          <div className="mt-4">
            <KickerLabel>Error</KickerLabel>
            <pre className="mt-1 whitespace-pre-wrap break-words rounded-md bg-zinc-50 p-3 text-xs text-zinc-800 ring-1 ring-zinc-200">{data.errorMessage}</pre>
          </div>
        ) : null}
      </TechnicalDetail>
    </div>
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
    { label: 'ASIC search', meaning: d.success ? 'done' : 'quiet', at: d.searchedAt, note: d.success ? undefined : 'failed', current: true },
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

function Row({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="grid grid-cols-[9rem_1fr] items-baseline gap-3">
      <dt className="truncate text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">{label}</dt>
      <dd className="font-mono text-sm tabular-nums text-zinc-900">{value}</dd>
    </div>
  )
}
