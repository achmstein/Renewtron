import { useMemo, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Copy, KeyRound, Link2, Mail, MoreHorizontal, RefreshCw, ShoppingCart, Users } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type AsicDeclareAs, type ClientAbnDto, type ClientBusinessNameDto, type ClientDetail } from '../api/client'
import { Cell, EmptyState, StatusPill } from './_ui'
import { Status, statusOf } from './_status'
import { DueIn, Queue, When, useRowKeyboard } from './_rows'
import { StoryStrip, type StoryStep } from './_record'
import { WorkRow } from './_work'
import { fmtDate, fmtMoney2, formatAbn, relativeTime } from './_utils'
import {
  Breadcrumb, BreadcrumbItem, BreadcrumbLink, BreadcrumbList, BreadcrumbPage, BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Label } from '@/components/ui/label'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { cn } from '@/lib/utils'

const errorText = (e: unknown) => (e instanceof Error ? e.message : undefined)
const firstName = (c: ClientDetail) => c.fullName.split(' ')[0] || 'this client'

export default function ClientDetails() {
  const { id } = useParams()
  const { data, isPending, isError } = useQuery({
    queryKey: ['admin-client', id],
    queryFn: () => api.admin.client(id!),
    enabled: !!id,
  })

  if (isPending) {
    return (
      <div className="mx-auto max-w-6xl space-y-6 px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
        <Skeleton className="h-5 w-48" />
        <Skeleton className="h-16 w-full max-w-80" />
        <Skeleton className="h-24 rounded-xl" />
        <Skeleton className="h-64 rounded-xl" />
      </div>
    )
  }
  if (isError || !data) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 text-center">
        <h3 className="text-heading">Client not found</h3>
        <p className="mt-1 text-caption text-muted-foreground">It may have been merged into another client.</p>
        <Button asChild variant="ghost" size="sm" className="mt-4"><Link to="/admin/clients">Back to clients</Link></Button>
      </div>
    )
  }

  const primary = data.emails.find((e) => e.isPrimary) ?? data.emails[0]
  const facts = [primary?.email, data.mobileNumber, `first seen ${relativeTime(data.createdAt)}`].filter(Boolean)
  return (
    <div className="mx-auto max-w-6xl space-y-6 px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem><BreadcrumbLink asChild><Link to="/admin/clients">Clients</Link></BreadcrumbLink></BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem><BreadcrumbPage>{data.fullName || 'Unnamed client'}</BreadcrumbPage></BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <h1 className="text-title">{data.fullName || 'Unnamed client'}</h1>
          <p className="mt-1 text-sm text-muted-foreground [overflow-wrap:anywhere]">{facts.join(' · ')}</p>
        </div>
        <div className="flex flex-wrap gap-1.5">
          {data.hasPurchased ? <StatusPill tone="emerald">Customer</StatusPill> : <StatusPill tone="zinc">Lead</StatusPill>}
          {data.reminderOptIn ? <StatusPill tone="indigo">Wants reminders</StatusPill> : null}
          {data.unsubscribedAt ? <StatusPill tone="red">Unsubscribed</StatusPill> : null}
          {data.isTest ? <StatusPill tone="amber">Test</StatusPill> : null}
        </div>
      </div>

      <SummaryStrip client={data} />

      <NeedsAttention client={data} />

      <DuplicatePanel client={data} />

      <Section title="Business names" description={`${activeNames(data).length} active, under ${data.abns.length} ABN${data.abns.length === 1 ? '' : 's'}`}>
        <BusinessNames client={data} />
      </Section>

      <div className="grid grid-cols-1 items-start gap-6 lg:grid-cols-3">
        <Section title="Activity" className="lg:col-span-2">
          <Timeline client={data} />
        </Section>
        <div className="space-y-6">
          <Section title="Contact details">
            <dl className="grid gap-4">
              <div className="grid grid-cols-2 gap-4">
                <Cell label="Date of birth" value={data.dateOfBirth ? fmtDate(data.dateOfBirth) : '—'} />
                <Cell label="TFN on file" value={data.hasTfn ? 'Yes' : 'No'} />
              </div>
              <Cell label="Address" value={data.address ?? '—'} />
              <Cell label="Emails" value={
                <ul className="space-y-1.5">
                  {data.emails.map((e) => (
                    <li key={e.id} title={e.ontraportContactId ? `Ontraport contact ${e.ontraportContactId}` : undefined}>
                      <div className="break-all">{e.email}</div>
                      <div className="text-caption text-muted-foreground">{e.isPrimary ? 'Primary · ' : ''}{e.source}</div>
                    </li>
                  ))}
                </ul>
              } />
            </dl>
          </Section>
          <Section title="Notes">
            <Notes client={data} />
          </Section>
        </div>
      </div>
    </div>
  )
}

/**
 * This client's items from the work queues, as Today shows them, so staff act on them here.
 * Possible duplicates are left to the panel below.
 */
function NeedsAttention({ client }: { client: ClientDetail }) {
  const { data } = useQuery({
    queryKey: ['admin-client-work', client.id],
    queryFn: () => api.admin.today(client.id),
  })
  useRowKeyboard()
  if (!data) return null
  const items = data.groups.flatMap((g) => g.items).filter((i) => i.kind !== 'duplicate')
  if (items.length === 0) {
    return (
      <Card className="flex-row items-center gap-2 px-4 py-3 text-sm text-muted-foreground">
        <span aria-hidden className="size-2 rounded-full bg-success" />
        Nothing needs you for {firstName(client)} right now.
      </Card>
    )
  }
  return (
    <Queue title="Needs attention" count={items.length}>
      {items.map((i) => <WorkRow key={`${i.kind}-${i.id}`} item={i} />)}
    </Queue>
  )
}

const activeNames = (c: ClientDetail) => c.abns.flatMap((a) => a.businessNames).filter((n) => n.status === 'Active')

/** The few facts staff look for first, under the client's name. */
function SummaryStrip({ client }: { client: ClientDetail }) {
  const active = activeNames(client)
  const today = new Date().toISOString().slice(0, 10)
  const next = active
    .filter((n) => n.renewalDueDate && n.renewalDueDate >= today)
    .sort((a, b) => a.renewalDueDate!.localeCompare(b.renewalDueDate!))[0]
  const withKey = active.filter((n) => n.asicKey).length
  const reminderAbns = client.abns.filter((a) => a.isReminderContact).length
  const renewed = client.renewals.filter((r) => r.status === 'Completed')
  const lastRenewed = renewed.map((r) => r.completedAt ?? r.initiatedAt).sort().reverse()[0]
  const others = [...new Set(client.abns.filter((a) => !a.isReminderContact).flatMap((a) => a.contacts.filter((c) => c.isReminderContact).map((c) => c.fullName)))]

  const items: Array<{ label: string; value: ReactNode; sub?: string; dot?: string }> = [
    { label: 'Business names', value: active.length, sub: active.length === 1 ? active[0].name : active.length ? `across ${client.abns.length} ABN${client.abns.length === 1 ? '' : 's'}` : 'none on the register yet' },
    { label: 'Next due', value: next ? fmtDate(next.renewalDueDate!) : 'Unknown', sub: next ? next.name : active.length ? 'no due date on file' : undefined },
    { label: 'ASIC keys', value: active.length ? `${withKey} of ${active.length}` : '—', sub: withKey === active.length && active.length ? 'all names' : undefined },
    {
      label: 'Reminders',
      value: client.unsubscribedAt ? 'Unsubscribed' : client.abns.length === 0 ? '—' : reminderAbns === client.abns.length ? 'Gets them' : reminderAbns === 0 ? 'Someone else' : `${reminderAbns} of ${client.abns.length} ABNs`,
      sub: others.length ? `others go to ${others.join(', ')}` : undefined,
      dot: client.unsubscribedAt ? 'bg-destructive' : undefined,
    },
    { label: 'Renewals with us', value: renewed.length, sub: lastRenewed ? `last ${fmtDate(lastRenewed)}` : client.sales.length ? `${client.sales.length} Ontraport sale${client.sales.length === 1 ? '' : 's'}` : undefined },
  ]
  return (
    <Card className="gap-0 overflow-hidden py-0">
      {/* Phones: two to a row, the first across the top. The lines are a 1px gap over the border colour. */}
      <dl className="grid grid-cols-2 gap-px bg-border sm:grid-cols-5 [&>*]:bg-card [&>*:first-child]:col-span-2 sm:[&>*:first-child]:col-span-1">
        {items.map((i) => (
          <div key={i.label} className="min-w-0 px-3 py-4 sm:px-5">
            <dt className="flex items-center gap-2 text-sm text-muted-foreground">
              {i.dot ? <span aria-hidden className={cn('size-2 rounded-full', i.dot)} /> : null}
              {i.label}
            </dt>
            <dd className="mt-1 truncate text-xl font-semibold tracking-tight tabular-nums sm:text-2xl">{i.value}</dd>
            {i.sub ? <dd className="line-clamp-2 text-caption text-muted-foreground [overflow-wrap:anywhere] sm:line-clamp-none sm:truncate" title={i.sub}>{i.sub}</dd> : null}
          </div>
        ))}
      </dl>
    </Card>
  )
}

type TimelineKind = 'renewal' | 'sale' | 'keyRequest' | 'lead' | 'email'
const KIND_LABELS: Record<TimelineKind, string> = { renewal: 'Renewals', sale: 'Ontraport sales', keyRequest: 'Key requests', lead: 'Wizard visits', email: 'Emails' }
const KIND_ICONS: Record<TimelineKind, ReactNode> = {
  renewal: <RefreshCw className="size-4" />,
  sale: <ShoppingCart className="size-4" />,
  keyRequest: <KeyRound className="size-4" />,
  lead: <Users className="size-4" />,
  email: <Mail className="size-4" />,
}
const TIMELINE_STEP = 15

/** Every record on the client, newest first, in one list; the toggles narrow it to one kind. */
function Timeline({ client }: { client: ClientDetail }) {
  const [kind, setKind] = useState<TimelineKind | ''>('')
  const [shown, setShown] = useState(TIMELINE_STEP)

  const events = useMemo(() => {
    const all: Array<{ key: string; kind: TimelineKind; at: string; to?: string; title: string; sub: string; status: string }> = [
      ...client.renewals.map((r) => ({
        key: `r${r.id}`, kind: 'renewal' as const, at: r.initiatedAt, to: `/admin/renewals/${r.id}`, title: `Renewal: ${r.businessName}`,
        sub: `${r.renewalYears} yr · ${fmtMoney2(r.amount)} · ${r.source}`, status: r.status,
      })),
      ...client.sales.map((s) => ({
        key: `s${s.id}`, kind: 'sale' as const, at: s.syncedAt, title: `Ontraport sale: ${s.businessName}`,
        sub: `${fmtMoney2(s.amountPaid)} · contact ${s.ontraportContactId}${s.renewalDueDate ? ` · due ${fmtDate(s.renewalDueDate)}` : ''}`, status: s.status,
      })),
      ...client.keyRequests.map((k) => ({
        key: `k${k.id}`, kind: 'keyRequest' as const, at: k.createdAt, title: `ASIC key request: ${k.businessName}`,
        sub: k.asicReferenceNumber ? `ref ${k.asicReferenceNumber}` : '', status: k.status,
      })),
      ...client.leads.map((l) => ({
        key: `l${l.id}`, kind: 'lead' as const, at: l.createdAt, to: `/admin/leads/${l.id}`, title: `Wizard visit: ABN ${formatAbn(l.abn)}`,
        sub: l.email, status: l.outcome,
      })),
      ...client.messages.map((m) => ({
        key: `m${m.id}`, kind: 'email' as const, at: m.createdAt, title: `Email: ${m.subject || m.template}`,
        sub: m.recipient, status: m.status,
      })),
    ]
    return all.sort((a, b) => b.at.localeCompare(a.at))
  }, [client])

  if (events.length === 0) return <p className="text-sm text-muted-foreground">Nothing here yet.</p>
  const counts = events.reduce<Partial<Record<TimelineKind, number>>>((acc, e) => ({ ...acc, [e.kind]: (acc[e.kind] ?? 0) + 1 }), {})
  const visible = events.filter((e) => !kind || e.kind === kind)
  const option = (value: TimelineKind | 'all', label: string, count: number) => (
    <ToggleGroupItem key={value} value={value} className="px-3">
      {label} <span className="text-muted-foreground tabular-nums">{count}</span>
    </ToggleGroupItem>
  )

  return (
    <div>
      {/* One row that scrolls sideways when it's wider than the screen (wrapping breaks the joined buttons). */}
      <div className="mb-3 overflow-x-auto [scrollbar-width:none]">
        <ToggleGroup type="single" variant="outline" size="sm" value={kind || 'all'}
          onValueChange={(v) => { if (!v) return; setKind(v === 'all' ? '' : (v as TimelineKind)); setShown(TIMELINE_STEP) }}>
          {option('all', 'All', events.length)}
          {(Object.keys(KIND_LABELS) as TimelineKind[]).filter((k) => counts[k]).map((k) => option(k, KIND_LABELS[k], counts[k]!))}
        </ToggleGroup>
      </div>
      <ol className="divide-y">
        {visible.slice(0, shown).map((e) => (
          <li key={e.key} className="flex items-start gap-3 py-2.5">
            <span aria-hidden className="mt-0.5 text-muted-foreground">{KIND_ICONS[e.kind]}</span>
            <div className="min-w-0 flex-1">
              {e.to ? <Link to={e.to} className="text-sm text-link hover:underline">{e.title}</Link> : <div className="text-sm">{e.title}</div>}
              <div className="truncate text-caption text-muted-foreground">
                <span title={new Date(e.at).toLocaleString()}>{fmtDate(e.at)}</span>{e.sub ? ` · ${e.sub}` : ''}
              </div>
            </div>
            <Status kind={e.kind} status={e.status} />
          </li>
        ))}
      </ol>
      {visible.length > shown ? (
        <Button variant="ghost" size="sm" className="mt-2" onClick={() => setShown((s) => s + TIMELINE_STEP * 2)}>
          Show {Math.min(visible.length - shown, TIMELINE_STEP * 2)} more
        </Button>
      ) : null}
    </div>
  )
}

/** "Shares an ABN with…": merge one way or the other, or keep them apart. */
function DuplicatePanel({ client }: { client: ClientDetail }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['admin-client'] })
    void queryClient.invalidateQueries({ queryKey: ['admin-clients'] })
    void queryClient.invalidateQueries({ queryKey: ['admin-sidebar-counts'] })
  }
  const merge = useMutation({
    mutationFn: (v: { from: string; into: string }) => api.admin.mergeClient(v.from, v.into),
    onSuccess: (_, v) => { refresh(); sileo.success({ title: 'Clients merged' }); navigate(`/admin/clients/${v.into}`) },
    onError: (e) => sileo.error({ title: 'Merge failed', description: errorText(e) }),
  })
  const dismiss = useMutation({
    mutationFn: (id: string) => api.admin.dismissClientDuplicate(id),
    onSuccess: () => { refresh(); sileo.success({ title: 'Kept as separate clients' }) },
    onError: (e) => sileo.error({ title: 'Could not update', description: errorText(e) }),
  })

  const others = [
    ...(client.possibleDuplicateOf ? [{ ...client.possibleDuplicateOf, flagged: client.id }] : []),
    ...client.duplicates.map((d) => ({ ...d, flagged: d.id })),
  ]
  if (others.length === 0) return null
  const busy = merge.isPending || dismiss.isPending
  const why = 'Another client on the same ABN has the same name or mobile, under a different email. It may be the same person, or two people at the business who share a phone. Merge only if it’s the same person: the ASIC key enquiry goes out in one person’s name.'

  return (
    <Card className="gap-0 overflow-hidden py-0">
      <div className="border-b px-4 py-3" title={why}>
        <h3 className="flex items-center gap-2 text-heading">
          <span aria-hidden className="size-2 rounded-full bg-warning" />
          Possible duplicate
        </h3>
        <p className="mt-0.5 text-caption text-muted-foreground">Same ABN and name or mobile, different email. Merge only if it’s the same person.</p>
      </div>
      {others.map((o) => (
        <div key={o.id} className="flex flex-wrap items-center gap-2 border-b px-4 py-3 last:border-b-0">
          <Link to={`/admin/clients/${o.id}`} className="mr-auto text-sm font-medium text-link hover:underline">{o.fullName || 'Unnamed client'}</Link>
          <Button size="sm" variant="outline" disabled={busy}
            onClick={() => merge.mutate({ from: o.id, into: client.id })}>Merge into this client</Button>
          <Button size="sm" variant="outline" disabled={busy}
            onClick={() => merge.mutate({ from: client.id, into: o.id })}>Merge this into them</Button>
          <Button size="sm" variant="ghost" disabled={busy} onClick={() => dismiss.mutate(o.flagged)}>Different people</Button>
        </div>
      ))}
    </Card>
  )
}

/** One group per ABN: who deals with us about it, who gets its reminders, and its business names. */
function BusinessNames({ client }: { client: ClientDetail }) {
  if (client.abns.length === 0) return <EmptyState title="No ABN yet" message="Business names appear once we have the client’s ABN." />
  return (
    <div className="space-y-4">
      {client.abns.map((a) => <AbnGroup key={a.abn} client={client} abn={a} />)}
    </div>
  )
}

function AbnGroup({ client, abn }: { client: ClientDetail; abn: ClientAbnDto }) {
  const queryClient = useQueryClient()
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['admin-client'] })
  const setStatus = useMutation({
    mutationFn: (v: { id: string; status: ClientBusinessNameDto['status'] }) => api.admin.setClientBusinessNameStatus(v.id, v.status),
    onSuccess: refresh,
    onError: (e) => sileo.error({ title: 'Could not update the name', description: errorText(e) }),
  })
  const makeReminder = useMutation({
    mutationFn: (clientId: string) => api.admin.setAbnReminderContact(abn.abn, clientId),
    onSuccess: () => { refresh(); sileo.success({ title: 'Reminder contact changed' }) },
    onError: (e) => sileo.error({ title: 'Could not change the reminder contact', description: errorText(e) }),
  })
  const reminderOther = abn.contacts.find((c) => c.isReminderContact)

  return (
    <div className="overflow-hidden rounded-lg border">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b bg-muted/40 px-3 py-2.5 sm:px-4">
        <div className="min-w-0" title={abn.registerCheckedAt ? `Register checked ${relativeTime(abn.registerCheckedAt)}` : 'Register not checked yet'}>
          <div className="text-sm font-medium">ABN <span className="font-mono">{formatAbn(abn.abn)}</span></div>
          <div className="mt-0.5 text-caption text-muted-foreground">
            {abn.contacts.length === 0 ? 'Only contact' : (
              <>Also on this ABN:{' '}
                {abn.contacts.map((c, i) => (
                  <span key={c.clientId}>
                    {i > 0 ? ', ' : ''}
                    <Link to={`/admin/clients/${c.clientId}`} className="text-link hover:underline">{c.fullName || 'Unnamed client'}</Link>
                    {c.isReminderContact ? ' (gets reminders)' : ''}
                  </span>
                ))}
              </>
            )}
          </div>
        </div>
        <div className="flex items-center gap-2">
          {abn.isReminderContact ? (
            <StatusPill tone="indigo">Gets reminders</StatusPill>
          ) : (
            <Button size="sm" variant="outline" disabled={makeReminder.isPending} onClick={() => makeReminder.mutate(client.id)}
              title={`Reminders go to ${reminderOther?.fullName || 'another contact'}`}>
              Send reminders to {firstName(client)}
            </Button>
          )}
        </div>
      </div>

      {abn.businessNames.length === 0 ? (
        <div className="px-4 py-3 text-sm text-muted-foreground">No business names on the register for this ABN.</div>
      ) : (
        <div className="divide-y">
          {abn.businessNames.map((n) => (
            <NameStory key={n.id} client={client} name={n} busy={setStatus.isPending} onStatus={(status) => setStatus.mutate({ id: n.id, status })} />
          ))}
        </div>
      )}
    </div>
  )
}

const sameName = (a: string | null | undefined, b: string) => (a ?? '').trim().toUpperCase() === b.trim().toUpperCase()
const newest = <T,>(xs: T[], at: (x: T) => string) => [...xs].sort((x, y) => at(y).localeCompare(at(x)))[0]

/**
 * One business name and its story with us: what was paid, the renewal, and the ASIC key.
 * Each step opens its record; names that are cancelled or left out stay to one quiet line.
 */
function NameStory({ client, name: n, busy, onStatus }: {
  client: ClientDetail
  name: ClientBusinessNameDto
  busy: boolean
  onStatus: (s: ClientBusinessNameDto['status']) => void
}) {
  const sale = newest(client.sales.filter((x) => sameName(x.businessName, n.name)), (x) => x.syncedAt)
  const renewal = newest(client.renewals.filter((x) => sameName(x.businessName, n.name)), (x) => x.initiatedAt)
  const keyRequest = newest(client.keyRequests.filter((x) => sameName(x.businessName, n.name)), (x) => x.createdAt)
  const active = n.status === 'Active'

  const due = n.renewalDueDate
    ? <>due <DueIn at={n.renewalDueDate} />{n.dueDateSource ? ` (from ${n.dueDateSource === 'Renewal' ? 'our renewal' : n.dueDateSource})` : ''}</>
    : <span title="No due date on file; renewals fall on the registration anniversary.">{anniversary(n.registrationDate)}</span>

  const steps: StoryStep[] = []
  if (sale) {
    steps.push({ label: `Paid ${fmtMoney2(sale.amountPaid)}`, meaning: 'done', at: sale.syncedAt, note: undefined, to: `/admin/ontraport-sales/${sale.id}` })
  } else if (renewal) {
    steps.push({ label: `Paid ${fmtMoney2(renewal.amount)}`, meaning: 'done', at: renewal.initiatedAt, note: renewal.source === 'Ontraport' ? 'Ontraport' : undefined })
  }
  if (renewal) {
    const st = statusOf('renewal', renewal.status)
    steps.push({ label: `Renewal: ${st.label.toLowerCase()}`, meaning: st.meaning, at: renewal.completedAt ?? renewal.initiatedAt, to: `/admin/renewals/${renewal.id}` })
    // ASIC took the renewal by BPAY: the bill is ours to pay at the bank.
    if (renewal.bpayReference) {
      steps.push(renewal.bpayPaidAt
        ? { label: 'BPAY bill paid', meaning: 'done', at: renewal.bpayPaidAt, to: `/admin/renewals/${renewal.id}` }
        : { label: 'BPAY bill', meaning: 'action', note: 'to pay', to: '/admin/bpay' })
    }
  } else if (sale) {
    const st = statusOf('sale', sale.status)
    steps.push({ label: 'Renewal', meaning: st.meaning, note: st.label.toLowerCase(), to: `/admin/ontraport-sales/${sale.id}` })
  }
  if (n.asicKey) {
    steps.push({ label: 'ASIC key', meaning: 'done', at: n.asicKeyReceivedAt, note: n.asicKeyReceivedAt ? undefined : 'on file' })
  } else if (keyRequest) {
    const st = statusOf('keyRequest', keyRequest.status)
    steps.push({ label: 'ASIC key', meaning: st.meaning, note: st.label.toLowerCase(), to: `/admin/asic-key-requests/${keyRequest.id}` })
  } else if (steps.length) {
    steps.push({ label: 'ASIC key', meaning: 'quiet', note: 'not requested' })
  }
  // Linked to our ASIC Connect account (and our email and representative on it).
  if (n.portalLink) {
    const st = statusOf('asicPortal', n.portalLink.status)
    steps.push({ label: 'Linked', meaning: st.meaning, at: n.portalLink.finishedAt ?? n.portalLink.createdAt, note: n.portalLink.status === 'Done' ? undefined : st.label.toLowerCase(), to: `/admin/asic-portal/${n.portalLink.id}` })
  } else if (n.portalLinkedAt) {
    steps.push({ label: 'Linked', meaning: 'done', at: n.portalLinkedAt })
  } else if (n.asicKey) {
    steps.push({ label: 'Linked', meaning: 'quiet', note: 'not yet' })
  }

  const meta = `${n.registrationDate ? `Registered ${fmtDate(n.registrationDate)}` : 'Registration date unknown'}${n.asicAccountNumber ? ` · ASIC account ${n.asicAccountNumber}` : ''} · via ${n.source}`

  return (
    <div className="px-3 py-3 sm:px-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0" title={meta}>
          <div className={cn('text-sm font-medium', !active && 'text-muted-foreground')}>{n.name}</div>
          <div className="text-caption text-muted-foreground">
            {active ? due : n.status === 'Excluded' ? 'Left out of reminders' : 'No longer on the register'}
            {n.lastRenewedAt ? <> · renewed <When at={n.lastRenewedAt} /> for {n.lastRenewalYears} yr</> : steps.length || !active ? null : ' · not renewed with us yet'}
            {n.asicKey ? <> · key <span className="font-mono">{n.asicKey}</span></> : null}
          </div>
          {/* Phones can't hover for the registration details: show them as a line. */}
          <div className="text-caption text-muted-foreground sm:hidden">{meta}</div>
        </div>
        <div className="flex items-center gap-1.5">
          <Status kind="businessName" status={n.status} />
          <NameMenu name={n} busy={busy} onStatus={onStatus} />
        </div>
      </div>
      {active && steps.length ? <div className="mt-2"><StoryStrip steps={steps} /></div> : null}
    </div>
  )
}

/** Exclude / include, and copying the ASIC key. */
function NameMenu({ name, busy, onStatus }: { name: ClientBusinessNameDto; busy: boolean; onStatus: (s: ClientBusinessNameDto['status']) => void }) {
  const [linking, setLinking] = useState(false)
  const running = name.portalLink?.status === 'Queued' || name.portalLink?.status === 'Running'
  const copyKey = () => {
    if (!name.asicKey) return
    void navigator.clipboard.writeText(name.asicKey).then(() => sileo.success({ title: 'ASIC key copied' }))
  }
  if (name.status === 'Cancelled' && !name.asicKey) return null
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button size="icon-sm" variant="ghost" aria-label={`Actions for ${name.name}`} disabled={busy}>
          <MoreHorizontal />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {name.asicKey ? <DropdownMenuItem onSelect={copyKey}><Copy /> Copy ASIC key</DropdownMenuItem> : null}
        {name.asicKey && name.status !== 'Cancelled'
          ? <DropdownMenuItem disabled={running} onSelect={() => setLinking(true)}><Link2 /> {running ? 'Linking to our ASIC account…' : 'Link to our ASIC account'}</DropdownMenuItem>
          : null}
        {name.status === 'Active' ? <DropdownMenuItem onSelect={() => onStatus('Excluded')}>Leave out of reminders</DropdownMenuItem> : null}
        {name.status === 'Excluded' ? <DropdownMenuItem onSelect={() => onStatus('Active')}>Include in reminders again</DropdownMenuItem> : null}
      </DropdownMenuContent>
      <LinkToAsicDialog name={linking ? name : null} onClose={() => setLinking(false)} />
    </DropdownMenu>
  )
}

/**
 * Starts "link to our ASIC account" for one name: link it with the client's key, then our business
 * email, the holder's email and our representative only. Each change is lodged with ASIC with a
 * declaration, so the person picks which one.
 */
function LinkToAsicDialog({ name, onClose }: { name: ClientBusinessNameDto | null; onClose: () => void }) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [declareAs, setDeclareAs] = useState<AsicDeclareAs>('Holder')
  const start = useMutation({ mutationFn: (n: ClientBusinessNameDto) => api.admin.linkToAsicPortal(n.id, declareAs) })
  const go = () => {
    if (!name) return
    void sileo.promise(start.mutateAsync(name), {
      loading: { title: 'Queuing…' },
      success: (r) => ({ title: r.message }),
      error: (e) => ({ title: 'Couldn’t start it', description: errorText(e) }),
    }).then((r) => {
      void queryClient.invalidateQueries({ queryKey: ['admin-client'] })
      onClose()
      navigate(`/admin/asic-portal/${r.id}`)
    }).catch(() => {})
  }
  return (
    <Dialog open={!!name} onOpenChange={(o) => { if (!o && !start.isPending) onClose() }}>
      <DialogContent className="sm:max-w-lg">
        {name ? (
          <>
            <DialogHeader>
              <DialogTitle>Link {name.name} to our ASIC account</DialogTitle>
              <DialogDescription>
                Links it with the client’s ASIC key, then lodges our business email, the holder’s email and removes every representative but ours.
              </DialogDescription>
            </DialogHeader>
            <DialogBody className="space-y-3">
              <Label>Declare each change</Label>
              <RadioGroup value={declareAs} onValueChange={(v) => setDeclareAs(v as AsicDeclareAs)}>
                <div className="flex items-start gap-2">
                  <RadioGroupItem id="declare-holder" value="Holder" className="mt-0.5" />
                  <Label htmlFor="declare-holder" className="font-normal">As the holder: “I am the registration or licence holder”</Label>
                </div>
                <div className="flex items-start gap-2">
                  <RadioGroupItem id="declare-behalf" value="OnBehalfOfHolder" className="mt-0.5" />
                  <Label htmlFor="declare-behalf" className="font-normal">On behalf of the holder: “submitting on behalf of the holder and authorised to”</Label>
                </div>
              </RadioGroup>
            </DialogBody>
            <DialogFooter>
              <Button variant="outline" onClick={onClose} disabled={start.isPending}>Cancel</Button>
              <Button onClick={go} disabled={start.isPending}>{start.isPending ? 'Starting…' : 'Link it'}</Button>
            </DialogFooter>
          </>
        ) : null}
      </DialogContent>
    </Dialog>
  )
}

/** Registration date (yyyy-MM-dd) to its anniversary, for names with no due date on file. */
function anniversary(registrationDate: string | null) {
  const m = /^\d{4}-(\d{2})-(\d{2})/.exec(registrationDate ?? '')
  if (!m) return 'unknown'
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
  return `Anniversary ${Number(m[2])} ${months[Number(m[1]) - 1] ?? ''}`
}

function Notes({ client }: { client: ClientDetail }) {
  const queryClient = useQueryClient()
  const [notes, setNotes] = useState(client.notes ?? '')
  const save = useMutation({
    mutationFn: () => api.admin.updateClientNotes(client.id, notes),
    onSuccess: () => { void queryClient.invalidateQueries({ queryKey: ['admin-client', client.id] }); sileo.success({ title: 'Notes saved' }) },
    onError: (e) => sileo.error({ title: 'Could not save', description: errorText(e) }),
  })
  return (
    <div>
      <Textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={5} maxLength={2000} placeholder="Anything the next person should know" />
      <div className="mt-2 flex justify-end">
        <Button size="sm" disabled={save.isPending || notes === (client.notes ?? '')} onClick={() => save.mutate()}>Save notes</Button>
      </div>
    </div>
  )
}

function Section({ title, description, action, children, className }: { title: string; description?: string; action?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <Card className={cn('gap-4', className)}>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        {description ? <CardDescription className="text-caption">{description}</CardDescription> : null}
        {action ? <CardAction>{action}</CardAction> : null}
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  )
}
