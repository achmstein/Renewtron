import { type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { Status, renewalStatus, statusOf } from './_status'
import { When } from './_rows'
import { NextStep, RecordCrumbs, RecordHeader, StoryStrip, TechnicalDetail, type StoryStep, DetailRow } from './_record'
import { fmtDateTime, fmtMoney2, formatAbn } from './_utils'
import { Skeletonizer } from '@/components/skeletonizer'
import { sample } from '@/components/skeleton-samples'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

type Detail = Awaited<ReturnType<typeof api.admin.lead>>
type Renewal = Detail['renewalRequests'][number]

function fmtDob(s: string) {
  const d = new Date(s)
  return `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}/${d.getFullYear()}`
}
const shortDate = (s: string) => new Date(s).toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })

// The page drawn as a skeleton while the visit loads.
const SAMPLE_LEAD: Detail = {
  id: sample.id(0),
  client: { id: sample.id(1), fullName: sample.name(0) },
  abn: sample.abn(0),
  fullName: sample.name(0),
  email: sample.email(0),
  mobileNumber: sample.mobile(),
  dateOfBirth: '1984-03-12',
  createdAt: sample.daysAgo(2),
  outcome: 'Converted',
  reminderOptIn: true,
  convertedToRenewal: true,
  convertedAt: sample.daysAgo(2),
  searchLog: {
    id: sample.id(2),
    searchedAt: sample.daysAgo(2),
    success: true,
    resultsCount: 2,
    results: [0, 1].map((i) => ({ id: sample.id(10 + i), businessName: sample.business(i), accountNumber: '612 345 678', registrationDate: sample.daysAgo(900 + i * 200) })),
  },
  renewalRequests: [{ id: sample.id(3), status: 'Completed', amount: 299, renewalYears: 3, initiatedAt: sample.daysAgo(2), businessName: sample.business(0) }],
}

export default function LeadDetails() {
  const { id } = useParams()
  const { data: loaded, isPending, isError } = useQuery({
    queryKey: ['admin-lead', id],
    queryFn: () => api.admin.lead(id!),
    enabled: !!id,
  })
  const data = loaded ?? (isPending ? SAMPLE_LEAD : undefined)


  if (isError || !data) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 text-center sm:px-6 lg:px-8">
        <h3 className="text-heading">Wizard visit not found</h3>
        <Button asChild variant="ghost" size="sm" className="mt-4"><Link to="/admin/leads">Back to leads</Link></Button>
      </div>
    )
  }

  const latest = [...data.renewalRequests].sort((a, b) => b.initiatedAt.localeCompare(a.initiatedAt))[0]
  const names = data.searchLog?.results ?? []

  return (
    <Skeletonizer loading={isPending}>
    <div className="mx-auto max-w-5xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <RecordCrumbs client={data.client} list={{ to: '/admin/leads', label: 'Leads' }} here={`Wizard visit of ${shortDate(data.createdAt)}`} />

      <RecordHeader
        title={headline(data, latest)}
        sub={<>{data.fullName} · ABN {formatAbn(data.abn)} · <When at={data.createdAt} />{data.reminderOptIn ? ' · asked for reminders' : ''}</>}
        status={latest ? renewalStatus(latest) : statusOf('lead', data.outcome)}
      >
        {nextStep(data, latest)}
        <StoryStrip steps={story(data, latest)} />
      </RecordHeader>

      <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-2">
        <Section title="Who visited">
          {data.client ? (
            <DetailRow label="Client" value={<Link to={`/admin/clients/${data.client.id}`} className="font-medium text-link hover:underline">{data.client.fullName || 'Unnamed client'}</Link>} />
          ) : null}
          <DetailRow label="Name" value={data.fullName} />
          <DetailRow label="Email" value={<a href={`mailto:${data.email}`} className="break-all text-link hover:underline">{data.email}</a>} />
          <DetailRow label="Mobile" value={<a href={`tel:${data.mobileNumber}`} className="text-link hover:underline">{data.mobileNumber}</a>} />
          <DetailRow label="Date of birth" value={fmtDob(data.dateOfBirth)} />
          <DetailRow label="Reminders" value={data.reminderOptIn ? 'Asked for renewal reminders' : 'Didn’t ask for reminders'} />
        </Section>

        <Section title={names.length ? `${names.length} business name${names.length === 1 ? '' : 's'} on the ABN` : 'Business names on the ABN'}>
          {names.length === 0 ? (
            <p className="text-sm text-muted-foreground">{data.searchLog ? 'ASIC found no business names on this ABN.' : 'No ASIC search is linked to this visit.'}</p>
          ) : names.map((n) => {
            const r = data.renewalRequests.filter((x) => x.businessName === n.businessName).sort((a, b) => b.initiatedAt.localeCompare(a.initiatedAt))[0]
            return (
              <div key={n.id} className="flex items-center justify-between gap-3 border-b pb-2 last:border-0 last:pb-0">
                <div className="min-w-0">
                  <div className="truncate text-sm font-medium">{n.businessName}</div>
                  <div className="text-caption text-muted-foreground">{r ? <>Renewal <When at={r.initiatedAt} /> · {fmtMoney2(r.amount)}</> : 'No renewal started'}</div>
                </div>
                {r ? <Link to={`/admin/renewals/${r.id}`} className="shrink-0"><Status entry={renewalStatus(r)} /></Link> : null}
              </div>
            )
          })}
        </Section>
      </div>

      <TechnicalDetail>
        <dl className="grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-2">
          <DetailRow label="Lead ID" value={<span className="break-all">{data.id}</span>} mono />
          <DetailRow label="Outcome" value={data.outcome} />
          <DetailRow label="Created" value={fmtDateTime(data.createdAt)} />
          <DetailRow label="Converted" value={data.convertedAt ? fmtDateTime(data.convertedAt) : '—'} />
          <DetailRow label="IP address" value={data.ipAddress ?? '—'} mono />
          <DetailRow label="Session ID" value={<span className="break-all">{data.sessionId ?? '—'}</span>} mono />
          <DetailRow label="User agent" value={<span className="break-all text-caption">{data.userAgent ?? '—'}</span>} />
          {data.searchLog ? <DetailRow label="Search" value={`${data.searchLog.success ? 'succeeded' : 'failed'} · ${data.searchLog.resultsCount} result(s) · ${fmtDateTime(data.searchLog.searchedAt)}`} /> : null}
          {names.map((n) => <DetailRow key={n.id} label={n.businessName} value={`acct ${n.accountNumber} · reg ${n.registrationDate}`} mono />)}
        </dl>
        {data.outcomeMessage || data.searchLog?.errorMessage ? (
          <div className="mt-4">
            <div className="text-caption text-muted-foreground">Messages</div>
            <pre className="mt-1 whitespace-pre-wrap break-words rounded-md border bg-muted/50 p-3 text-caption">{[data.outcomeMessage, data.searchLog?.errorMessage].filter(Boolean).join('\n')}</pre>
          </div>
        ) : null}
      </TechnicalDetail>
    </div>
    </Skeletonizer>
  )
}

/** What the visit came to, in words. */
function headline(d: Detail, latest: Renewal | undefined): string {
  const who = d.fullName || 'Someone'
  if (latest?.status === 'Completed' || d.outcome === 'RenewalCompleted') return `${who} renewed with us`
  if (latest && (latest.status === 'Pending' || latest.status === 'Processing')) return `${who} paid; the renewal is under way`
  if (latest?.status === 'Failed') return `${who}’s renewal failed`
  switch (d.outcome) {
    case 'RenewalAvailable': return `${who} found a name due and didn’t pay`
    case 'NotDueForRenewal': return `${who} looked up their names; nothing was due`
    case 'RenewalInProgress': return `${who} is paying`
    case 'NoBusinessNames': return `${who}’s ABN has no business names`
    default: return `${who} started the wizard; the ASIC check didn’t finish`
  }
}

function nextStep(d: Detail, latest: Renewal | undefined): ReactNode {
  const openClient = d.client ? <Button asChild size="sm" variant="outline"><Link to={`/admin/clients/${d.client.id}`}>Open client</Link></Button> : null
  if (latest && latest.status !== 'Completed') {
    const s = renewalStatus(latest)
    return (
      <NextStep meaning={s.meaning} title={s.meaning === 'action' ? 'The renewal needs a person.' : 'Nothing to do here; follow the renewal.'}
        actions={<Button asChild size="sm"><Link to={`/admin/renewals/${latest.id}`}>Open renewal</Link></Button>}>
        {latest.businessName ?? 'Renewal'}: {s.label.toLowerCase()}, started <When at={latest.initiatedAt} />.
      </NextStep>
    )
  }
  if (latest) {
    return <NextStep meaning="done" title="Nothing to do." actions={openClient}>Renewed <When at={latest.initiatedAt} />. Any other names on the ABN are on the client’s page.</NextStep>
  }
  if (d.outcome === 'RenewalAvailable') {
    return (
      <NextStep meaning="waiting" title="Waiting on them to pay." actions={openClient}>
        {d.reminderOptIn ? 'They’ll get a reminder before it’s due.' : 'No reminders asked for; win-back emails can reach them.'}
      </NextStep>
    )
  }
  if (d.outcome === 'NotDueForRenewal') {
    return <NextStep meaning="quiet" title="Nothing to do." actions={openClient}>{d.reminderOptIn ? 'We’ll remind them when a name comes due.' : 'They didn’t ask for reminders.'}</NextStep>
  }
  return <NextStep meaning="quiet" title="Nothing to do." actions={openClient}>No business name here can be renewed.</NextStep>
}

/** Search → visit → renewal. */
function story(d: Detail, latest: Renewal | undefined): StoryStep[] {
  const steps: StoryStep[] = []
  if (d.searchLog) {
    steps.push({
      label: 'ASIC search',
      meaning: d.searchLog.success ? 'done' : 'action',
      at: d.searchLog.searchedAt,
      note: d.searchLog.success ? undefined : 'failed',
      to: `/admin/searches/${d.searchLog.id}`,
    })
  }
  steps.push({ label: 'Wizard visit', meaning: 'done', at: d.createdAt, current: true })
  if (latest) {
    const s = renewalStatus(latest)
    steps.push({ label: `Renewal: ${s.label.toLowerCase()}`, meaning: s.meaning, at: latest.initiatedAt, to: `/admin/renewals/${latest.id}` })
  } else {
    steps.push({ label: 'Renewal', meaning: 'quiet', note: d.outcome === 'RenewalAvailable' ? 'not paid' : 'nothing due' })
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

