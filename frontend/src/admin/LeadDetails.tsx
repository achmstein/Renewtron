import { type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Loader2 } from 'lucide-react'
import { api } from '../api/client'
import { KickerLabel } from './_ui'
import { Status, renewalStatus, statusOf } from './_status'
import { When } from './_rows'
import { NextStep, RecordCrumbs, RecordHeader, StoryStrip, TechnicalDetail, type StoryStep } from './_record'
import { fmtDateTime, fmtMoney2, formatAbn } from './_utils'
import { Button } from '@/components/ui/button'

type Detail = Awaited<ReturnType<typeof api.admin.lead>>
type Renewal = Detail['renewalRequests'][number]

function fmtDob(s: string) {
  const d = new Date(s)
  return `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}/${d.getFullYear()}`
}
const shortDate = (s: string) => new Date(s).toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })

export default function LeadDetails() {
  const { id } = useParams()
  const { data, isPending, isError } = useQuery({
    queryKey: ['admin-lead', id],
    queryFn: () => api.admin.lead(id!),
    enabled: !!id,
  })

  if (isPending) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 text-center sm:px-6 lg:px-8">
        <Loader2 className="mx-auto h-8 w-8 animate-spin text-link" />
        <p className="mt-3 text-sm text-muted-foreground">Loading…</p>
      </div>
    )
  }

  if (isError || !data) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 text-center sm:px-6 lg:px-8">
        <h3 className="text-base font-semibold text-foreground">Wizard visit not found</h3>
        <p className="mt-1 text-sm text-muted-foreground">The lead you’re looking for doesn’t exist.</p>
        <Link to="/admin/leads" className="mt-4 inline-flex items-center text-sm font-medium text-link hover:text-link">← Back to leads</Link>
      </div>
    )
  }

  const latest = [...data.renewalRequests].sort((a, b) => b.initiatedAt.localeCompare(a.initiatedAt))[0]
  const names = data.searchLog?.results ?? []

  return (
    <div className="mx-auto max-w-5xl px-4 py-8 sm:px-6 lg:px-8">
      <RecordCrumbs client={data.client} list={{ to: '/admin/leads', label: 'Leads' }} here={`Wizard visit of ${shortDate(data.createdAt)}`} />

      <RecordHeader
        kicker={`Wizard visit · ABN ${formatAbn(data.abn)}`}
        title={headline(data, latest)}
        sub={<>{data.fullName} · {data.email} · <When at={data.createdAt} />{data.reminderOptIn ? ' · asked for reminders' : ''}</>}
        status={latest ? renewalStatus(latest) : statusOf('lead', data.outcome)}
      >
        {nextStep(data, latest)}
        <StoryStrip steps={story(data, latest)} />
      </RecordHeader>

      <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-2">
        <Section kicker="Person" title="Who visited">
          {data.client ? (
            <Row label="Client" value={<Link to={`/admin/clients/${data.client.id}`} className="font-medium text-link hover:underline">{data.client.fullName || 'Unnamed client'}</Link>} />
          ) : null}
          <Row label="Name" value={data.fullName} />
          <Row label="Email" value={<a href={`mailto:${data.email}`} className="break-all text-link hover:text-link">{data.email}</a>} />
          <Row label="Mobile" value={<a href={`tel:${data.mobileNumber}`} className="text-link hover:text-link">{data.mobileNumber}</a>} mono />
          <Row label="Date of birth" value={fmtDob(data.dateOfBirth)} mono />
          <Row label="Reminders" value={data.reminderOptIn ? 'Asked for renewal reminders' : 'Didn’t ask for reminders'} />
        </Section>

        <Section kicker="Names" title={names.length ? `${names.length} business name${names.length === 1 ? '' : 's'} on the ABN` : 'Business names on the ABN'}>
          {names.length === 0 ? (
            <p className="text-sm text-muted-foreground">{data.searchLog ? 'ASIC found no business names on this ABN.' : 'No ASIC search is linked to this visit.'}</p>
          ) : names.map((n) => {
            const r = data.renewalRequests.filter((x) => x.businessName === n.businessName).sort((a, b) => b.initiatedAt.localeCompare(a.initiatedAt))[0]
            return (
              <div key={n.id} className="flex items-center justify-between gap-3 border-b border-border pb-2 last:border-0 last:pb-0">
                <div className="min-w-0">
                  <div className="truncate text-sm font-medium text-foreground">{n.businessName}</div>
                  <div className="text-xs text-muted-foreground">{r ? <>Renewal <When at={r.initiatedAt} /> · {fmtMoney2(r.amount)}</> : 'No renewal started'}</div>
                </div>
                {r ? <Link to={`/admin/renewals/${r.id}`}><Status entry={renewalStatus(r)} /></Link> : null}
              </div>
            )
          })}
        </Section>
      </div>

      <TechnicalDetail>
        <dl className="grid grid-cols-1 gap-x-8 gap-y-3 lg:grid-cols-2">
          <Row label="Lead ID" value={<span className="break-all">{data.id}</span>} mono />
          <Row label="Outcome" value={data.outcome} mono />
          <Row label="Created" value={fmtDateTime(data.createdAt)} mono />
          <Row label="Converted" value={data.convertedAt ? fmtDateTime(data.convertedAt) : '—'} mono />
          <Row label="IP address" value={data.ipAddress ?? '—'} mono />
          <Row label="Session ID" value={<span className="break-all">{data.sessionId ?? '—'}</span>} mono />
          <Row label="User agent" value={<span className="break-all text-xs">{data.userAgent ?? '—'}</span>} mono />
          {data.searchLog ? <Row label="Search" value={`${data.searchLog.success ? 'succeeded' : 'failed'} · ${data.searchLog.resultsCount} result(s) · ${fmtDateTime(data.searchLog.searchedAt)}`} mono /> : null}
          {names.map((n) => <Row key={n.id} label={n.businessName} value={`acct ${n.accountNumber} · reg ${n.registrationDate}`} mono />)}
        </dl>
        {data.outcomeMessage || data.searchLog?.errorMessage ? (
          <div className="mt-4">
            <KickerLabel>Messages</KickerLabel>
            <pre className="mt-1 whitespace-pre-wrap break-words rounded-md bg-muted/50 p-3 text-xs text-foreground ring-1 ring-border">{[data.outcomeMessage, data.searchLog?.errorMessage].filter(Boolean).join('\n')}</pre>
          </div>
        ) : null}
      </TechnicalDetail>
    </div>
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
        {d.reminderOptIn ? 'They asked for reminders, so they’ll hear from us before it’s due.' : 'They didn’t ask for reminders; win-back emails from Leads can reach them.'}
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

function Section({ kicker, title, children }: { kicker: string; title: string; children: ReactNode }) {
  return (
    <div className="rounded-xl bg-card p-5 ring-1 ring-border shadow-sm">
      <div className="mb-4">
        <KickerLabel>{kicker}</KickerLabel>
        <h2 className="mt-0.5 text-base font-semibold tracking-tight text-foreground">{title}</h2>
      </div>
      <dl className="space-y-3">{children}</dl>
    </div>
  )
}

function Row({ label, value, mono }: { label: string; value: ReactNode; mono?: boolean }) {
  return (
    <div className="grid grid-cols-[9rem_1fr] items-baseline gap-3">
      <dt className="truncate text-xs text-muted-foreground">{label}</dt>
      <dd className={`text-sm text-foreground ${mono ? 'font-mono tabular-nums' : ''}`}>{value}</dd>
    </div>
  )
}
