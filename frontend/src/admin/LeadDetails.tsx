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
import { pageClass } from './_ui'

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
  outcome: 'RenewalAvailable',
  progress: 'Paid',
  namesShownAt: sample.daysAgo(2),
  reachedPaymentAt: sample.daysAgo(2),
  paymentFollowUpSentAt: null,
  reminderOptIn: true,
  convertedToRenewal: true,
  convertedAt: sample.daysAgo(2),
  searchLog: {
    id: sample.id(2),
    searchedAt: sample.daysAgo(2),
    success: true,
    resultsCount: 2,
    results: [0, 1].map((i) => ({ id: sample.id(10 + i), businessName: sample.business(i), accountNumber: '612 345 678', registrationDate: sample.daysAgo(900 + i * 200), isAvailable: true })),
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
      <div className={pageClass('py-12 text-center')}>
        <h3 className="text-heading">Lead not found</h3>
        <Button asChild variant="ghost" size="sm" className="mt-4"><Link to="/admin/leads">Back to leads</Link></Button>
      </div>
    )
  }

  const latest = [...data.renewalRequests].sort((a, b) => b.initiatedAt.localeCompare(a.initiatedAt))[0]
  const names = data.searchLog?.results ?? []

  return (
    <Skeletonizer loading={isPending}>
    <div className={pageClass()}>
      <RecordCrumbs client={data.client} list={{ to: '/admin/leads', label: 'Leads' }} here={`Lead of ${shortDate(data.createdAt)}`} />

      <RecordHeader
        title={headline(data, latest)}
        sub={<>{data.fullName} · ABN {formatAbn(data.abn)} · <When at={data.createdAt} />{data.reminderOptIn ? ' · asked for reminders' : ''}</>}
        status={latest ? renewalStatus(latest) : statusOf('lead', data.outcome)}
      >
        {nextStep(data, latest)}
      </RecordHeader>

      <div className="mt-6"><StoryStrip steps={story(data, latest)} /></div>

      <Card className="mt-6 gap-0 py-0">
        <CardHeader className="border-b py-4"><CardTitle>What ASIC said</CardTitle></CardHeader>
        <CardContent className="space-y-1 px-4 py-4 text-sm sm:px-6">
          <p>{asicSaid(data)}</p>
          {data.searchLog ? <p className="text-caption text-muted-foreground">Asked <When at={data.searchLog.searchedAt} />{data.searchLog.success ? '' : ' (ASIC didn’t answer the first lookup; the wizard used the register and checked again)'}.</p> : null}
        </CardContent>
      </Card>

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
            <p className="text-sm text-muted-foreground">{data.searchLog ? 'ASIC found no business names on this ABN.' : 'No ASIC search is linked to this lead.'}</p>
          ) : names.map((n) => {
            const r = data.renewalRequests.filter((x) => x.businessName === n.businessName).sort((a, b) => b.initiatedAt.localeCompare(a.initiatedAt))[0]
            return (
              <div key={n.id} className="flex items-center justify-between gap-3 border-b pb-2 last:border-0 last:pb-0">
                <div className="min-w-0">
                  <div className="truncate text-sm font-medium">{n.businessName}</div>
                  <div className="text-caption text-muted-foreground">{r ? <>Paid <When at={r.initiatedAt} /> · {fmtMoney2(r.amount)}</> : n.isAvailable ? 'Offered for renewal; not paid' : 'ASIC wouldn’t renew it then'}</div>
                </div>
                {r ? <Link to={r.orderId ? `/admin/renewal-orders/${r.orderId}` : `/admin/renewals/${r.id}`} className="shrink-0"><Status entry={renewalStatus(r)} /></Link> : null}
              </div>
            )
          })}
        </Section>
      </div>

      <TechnicalDetail>
        <dl className="grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-2">
          <DetailRow label="Lead ID" value={<span className="break-all">{data.id}</span>} mono />
          <DetailRow label="ASIC said" value={data.outcome} />
          <DetailRow label="Got as far as" value={data.progress} />
          <DetailRow label="Payment page" value={data.reachedPaymentAt ? fmtDateTime(data.reachedPaymentAt) : '—'} />
          <DetailRow label="Follow-up email" value={data.paymentFollowUpSentAt ? fmtDateTime(data.paymentFollowUpSentAt) : '—'} />
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

/** What the visit came to, in words: how far they got first, then why. */
function headline(d: Detail, latest: Renewal | undefined): string {
  const who = d.fullName || 'Someone'
  if (latest?.status === 'Completed') return `${who} paid, and it’s renewed`
  if (latest && (latest.status === 'Pending' || latest.status === 'Processing')) return `${who} paid; the renewal is under way`
  if (latest?.status === 'Failed') return `${who} paid; the renewal needs a look`
  if (d.progress === 'Paid') return `${who} paid`
  if (d.progress === 'PaymentPage') return `${who} reached the payment page and left without paying`
  if (d.progress === 'NamesShown') return `${who} saw their names and didn’t go on to pay`
  switch (d.outcome) {
    case 'NotDueForRenewal': return `${who} looked up their names; ASIC says nothing is due`
    case 'RenewalInProgress': return `${who} couldn’t pay: ASIC already has a renewal open for this ABN`
    case 'NoBusinessNames': return `${who}’s ABN has no business names`
    default: return `${who} gave their details; the ASIC check didn’t finish`
  }
}

/** ASIC's answer, in words; never how far the visitor got. */
function asicSaid(d: Detail): string {
  const offered = d.searchLog?.results.filter((r) => r.isAvailable).length ?? 0
  switch (d.outcome) {
    case 'RenewalAvailable': return offered
      ? `ASIC offered ${offered} business name${offered === 1 ? '' : 's'} on this ABN for renewal.`
      : 'ASIC offered names on this ABN for renewal.'
    case 'NotDueForRenewal': return 'ASIC said no name on this ABN is due for renewal yet.'
    case 'RenewalInProgress': return 'ASIC already has a renewal open for this ABN (someone started one at ASIC).'
    case 'NoBusinessNames': return 'ASIC has no business names on this ABN.'
    default: return 'ASIC hadn’t answered when they left.'
  }
}

function nextStep(d: Detail, latest: Renewal | undefined): ReactNode {
  const openClient = d.client ? <Button asChild size="sm" variant="outline"><Link to={`/admin/clients/${d.client.id}`}>Open client</Link></Button> : null
  if (latest && latest.status !== 'Completed') {
    const s = renewalStatus(latest)
    const to = latest.orderId ? `/admin/renewal-orders/${latest.orderId}` : `/admin/renewals/${latest.id}`
    return (
      <NextStep meaning={s.meaning} title={s.meaning === 'action' ? 'The renewal needs a person.' : 'Nothing to do here; follow the renewal.'}
        actions={<Button asChild size="sm"><Link to={to}>Open renewal</Link></Button>}>
        {latest.businessName ?? 'Renewal'}: {s.label.toLowerCase()}, paid <When at={latest.initiatedAt} />.
      </NextStep>
    )
  }
  if (latest) {
    return <NextStep meaning="done" title="Nothing to do." actions={openClient}>Renewed. Any other names on the ABN are on the client’s page.</NextStep>
  }
  if (d.progress === 'PaymentPage') {
    return (
      <NextStep meaning="waiting" title="Waiting on them to pay." actions={openClient}>
        {d.paymentFollowUpSentAt ? <>The “payment not finished” email went <When at={d.paymentFollowUpSentAt} />.</> : 'The “payment not finished” email goes on its own a couple of hours after they left.'}
      </NextStep>
    )
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

/** Details → names shown → payment page → paid → renewed: the same steps on every visit. */
function story(d: Detail, latest: Renewal | undefined): StoryStep[] {
  const rank = { Details: 0, NamesShown: 1, PaymentPage: 2, Paid: 3 }[d.progress] ?? 0
  const reached = (n: number) => rank >= n
  const stopped = (n: number) => rank === n - 1 && !latest
  const steps: StoryStep[] = [
    { label: 'Details', meaning: 'done', at: d.createdAt, current: true },
    { label: 'Names shown', meaning: reached(1) ? 'done' : 'quiet', at: d.namesShownAt,
      note: reached(1) ? undefined : stopped(1) ? asicShort(d.outcome) : undefined },
    { label: 'Payment page', meaning: reached(2) ? 'done' : 'quiet', at: d.reachedPaymentAt, note: reached(2) ? undefined : stopped(2) ? 'Left here' : undefined },
    { label: 'Paid', meaning: reached(3) ? 'done' : rank === 2 ? 'waiting' : 'quiet', at: d.convertedAt, note: !reached(3) && rank === 2 ? 'Left the payment page' : undefined },
  ]
  if (latest) {
    const s = renewalStatus(latest)
    steps.push({ label: 'Renewed', meaning: latest.status === 'Completed' ? 'done' : s.meaning, at: latest.status === 'Completed' ? latest.initiatedAt : undefined,
      note: latest.status === 'Completed' ? undefined : s.label, to: latest.orderId ? `/admin/renewal-orders/${latest.orderId}` : `/admin/renewals/${latest.id}` })
  } else {
    steps.push({ label: 'Renewed', meaning: 'quiet' })
  }
  return steps
}

function asicShort(outcome: string): string {
  switch (outcome) {
    case 'NotDueForRenewal': return 'ASIC: not due'
    case 'RenewalInProgress': return 'ASIC: renewal open'
    case 'NoBusinessNames': return 'ASIC: no names'
    default: return 'Left here'
  }
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Card className="gap-4">
      <CardHeader><CardTitle>{title}</CardTitle></CardHeader>
      <CardContent><dl className="space-y-3">{children}</dl></CardContent>
    </Card>
  )
}

