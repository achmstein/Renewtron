import { useMemo, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronLeft, Copy, KeyRound, Loader2, Mail, MoreHorizontal, RefreshCw, ShoppingCart, Users } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type ClientAbnDto, type ClientBusinessNameDto, type ClientDetail } from '../api/client'
import { EmptyState, KickerLabel, StatusPill } from './_ui'
import { Status } from './_status'
import { fmtDate, fmtMoney2, formatAbn, relativeTime } from './_utils'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'

const errorText = (e: unknown) => (e instanceof Error ? e.message : undefined)

export default function ClientDetails() {
  const { id } = useParams()
  const { data, isPending, isError } = useQuery({
    queryKey: ['admin-client', id],
    queryFn: () => api.admin.client(id!),
    enabled: !!id,
  })

  if (isPending) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 text-center">
        <Loader2 className="mx-auto h-8 w-8 animate-spin text-brand-600" />
      </div>
    )
  }
  if (isError || !data) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 text-center">
        <h3 className="text-base font-semibold text-zinc-900">Client not found</h3>
        <p className="mt-1 text-sm text-zinc-500">It may have been merged into another client.</p>
        <Link to="/admin/clients" className="mt-4 inline-flex text-sm font-medium text-brand-700">← Back to clients</Link>
      </div>
    )
  }

  const primary = data.emails.find((e) => e.isPrimary) ?? data.emails[0]
  return (
    <div className="mx-auto max-w-6xl px-4 py-8 sm:px-6 lg:px-8">
      <div className="mb-6">
        <Link to="/admin/clients" className="inline-flex items-center gap-1 text-xs font-mono uppercase tracking-[0.14em] text-zinc-500 hover:text-zinc-900">
          <ChevronLeft className="h-3 w-3" /> Back to clients
        </Link>
      </div>

      <div className="rounded-2xl bg-white ring-1 ring-zinc-200 shadow-sm">
        <div className="flex items-start justify-between gap-4 flex-wrap p-6">
          <div className="min-w-0">
            <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">CLIENT</div>
            <h1 className="mt-1 text-2xl font-semibold text-zinc-900 tracking-tight">{data.fullName || '—'}</h1>
            <div className="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-sm text-zinc-500">
              {primary ? <span className="font-mono">{primary.email}</span> : null}
              {data.mobileNumber ? <><span>·</span><span className="font-mono">{data.mobileNumber}</span></> : null}
              <span>·</span><span>first seen {relativeTime(data.createdAt)}</span>
            </div>
          </div>
          <div className="flex flex-wrap gap-1">
            {data.hasPurchased ? <StatusPill tone="emerald">CUSTOMER</StatusPill> : <StatusPill tone="zinc">LEAD</StatusPill>}
            {data.reminderOptIn ? <StatusPill tone="indigo">REMINDER OPT-IN</StatusPill> : null}
            {data.unsubscribedAt ? <StatusPill tone="red">UNSUBSCRIBED</StatusPill> : null}
            {data.isTest ? <StatusPill tone="amber">TEST</StatusPill> : null}
          </div>
        </div>
        <SummaryStrip client={data} />
      </div>

      <DuplicatePanel client={data} />

      <Section kicker="BUSINESS NAMES" title={`${activeNames(data).length} active, under ${data.abns.length} ABN${data.abns.length === 1 ? '' : 's'}`}>
        <BusinessNames client={data} />
      </Section>

      <div className="mt-6 grid grid-cols-1 lg:grid-cols-3 gap-6 items-start">
        <Section kicker="ACTIVITY" title="Everything we've done with them" className="lg:col-span-2">
          <Timeline client={data} />
        </Section>
        <div className="space-y-6">
          <Section kicker="PROFILE" title="Contact details" className="">
            <dl className="space-y-3">
              <Row label="Date of birth" value={data.dateOfBirth ? fmtDate(data.dateOfBirth) : '—'} mono />
              <Row label="Address" value={data.address ?? '—'} />
              <Row label="TFN on file" value={data.hasTfn ? 'Yes' : 'No'} />
              <Row label="Emails" value={
                <ul className="space-y-1.5">
                  {data.emails.map((e) => (
                    <li key={e.id} className="text-xs">
                      <div className="font-mono break-all">{e.email}</div>
                      <div className="text-zinc-400">{e.isPrimary ? 'Primary · ' : ''}{e.source}{e.ontraportContactId ? ` · Ontraport ${e.ontraportContactId}` : ''}</div>
                    </li>
                  ))}
                </ul>
              } />
            </dl>
          </Section>
          <Section kicker="NOTES" title="Staff notes" className="">
            <Notes client={data} />
          </Section>
        </div>
      </div>
    </div>
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

  const items: Array<{ label: string; value: ReactNode; sub?: ReactNode }> = [
    { label: 'Business names', value: active.length, sub: active.length === 1 ? active[0].name : active.length ? `across ${client.abns.length} ABN${client.abns.length === 1 ? '' : 's'}` : 'none on the register yet' },
    { label: 'Next due', value: next ? fmtDate(next.renewalDueDate!) : 'Unknown', sub: next ? next.name : active.length ? 'no due date on file' : undefined },
    { label: 'ASIC keys', value: active.length ? `${withKey} of ${active.length}` : '—', sub: withKey === active.length && active.length ? 'all names' : undefined },
    {
      label: 'Reminders',
      value: client.unsubscribedAt ? 'Unsubscribed' : client.abns.length === 0 ? '—' : reminderAbns === client.abns.length ? 'Gets them' : reminderAbns === 0 ? 'Someone else' : `${reminderAbns} of ${client.abns.length} ABNs`,
      sub: others.length ? `others go to ${others.join(', ')}` : undefined,
    },
    { label: 'Renewals with us', value: renewed.length, sub: lastRenewed ? `last ${fmtDate(lastRenewed)}` : client.sales.length ? `${client.sales.length} Ontraport sale${client.sales.length === 1 ? '' : 's'}` : undefined },
  ]
  return (
    <dl className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 border-t border-zinc-100">
      {items.map((i) => (
        <div key={i.label} className="px-6 py-4 border-zinc-100 [&:not(:first-child)]:lg:border-l">
          <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">{i.label}</dt>
          <dd className="mt-1 text-lg font-semibold text-zinc-900 tabular-nums">{i.value}</dd>
          {i.sub ? <dd className="text-xs text-zinc-500 truncate" title={typeof i.sub === 'string' ? i.sub : undefined}>{i.sub}</dd> : null}
        </div>
      ))}
    </dl>
  )
}

type TimelineKind = 'renewal' | 'sale' | 'keyRequest' | 'lead' | 'email'
const KIND_LABELS: Record<TimelineKind, string> = { renewal: 'Renewals', sale: 'Ontraport sales', keyRequest: 'Key requests', lead: 'Wizard visits', email: 'Emails' }
const KIND_ICONS: Record<TimelineKind, ReactNode> = {
  renewal: <RefreshCw className="h-3.5 w-3.5" />,
  sale: <ShoppingCart className="h-3.5 w-3.5" />,
  keyRequest: <KeyRound className="h-3.5 w-3.5" />,
  lead: <Users className="h-3.5 w-3.5" />,
  email: <Mail className="h-3.5 w-3.5" />,
}
const TIMELINE_STEP = 15

/** Every record on the client, newest first, in one list; chips narrow it to one kind. */
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

  if (events.length === 0) return <div className="text-sm text-zinc-400">Nothing yet.</div>
  const counts = events.reduce<Partial<Record<TimelineKind, number>>>((acc, e) => ({ ...acc, [e.kind]: (acc[e.kind] ?? 0) + 1 }), {})
  const visible = events.filter((e) => !kind || e.kind === kind)
  const chip = (value: TimelineKind | '', label: string, count: number) => (
    <button key={value || 'all'} type="button" onClick={() => { setKind(value); setShown(TIMELINE_STEP) }}
      className={`rounded-full px-2.5 py-1 text-xs ring-1 ${kind === value ? 'bg-zinc-900 text-white ring-zinc-900' : 'bg-white text-zinc-600 ring-zinc-200 hover:bg-zinc-50'}`}>
      {label} <span className="tabular-nums opacity-60">{count}</span>
    </button>
  )

  return (
    <div>
      <div className="mb-4 flex flex-wrap gap-1.5">
        {chip('', 'All', events.length)}
        {(Object.keys(KIND_LABELS) as TimelineKind[]).filter((k) => counts[k]).map((k) => chip(k, KIND_LABELS[k], counts[k]!))}
      </div>
      <ol className="relative border-l border-zinc-200 ml-2">
        {visible.slice(0, shown).map((e) => (
          <li key={e.key} className="relative pl-6 pb-4 last:pb-0">
            <span className="absolute -left-[11px] top-0.5 flex h-5 w-5 items-center justify-center rounded-full bg-white text-zinc-500 ring-1 ring-zinc-200">{KIND_ICONS[e.kind]}</span>
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                {e.to ? <Link to={e.to} className="text-sm text-zinc-900 hover:underline">{e.title}</Link> : <div className="text-sm text-zinc-900">{e.title}</div>}
                <div className="text-xs text-zinc-500">
                  <span title={new Date(e.at).toLocaleString()}>{fmtDate(e.at)}</span>{e.sub ? ` · ${e.sub}` : ''}
                </div>
              </div>
              <Status kind={e.kind} status={e.status} />
            </div>
          </li>
        ))}
      </ol>
      {visible.length > shown ? (
        <Button variant="ghost" size="sm" className="mt-3" onClick={() => setShown((s) => s + TIMELINE_STEP * 2)}>
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

  return (
    <div className="mt-6 rounded-xl bg-amber-50 p-5 ring-1 ring-amber-200">
      <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-amber-800">POSSIBLE DUPLICATE</div>
      <p className="mt-1 text-sm text-amber-900 max-w-3xl">
        Another client on the same ABN has the same name or mobile, under a different email. It may be the same person, or two
        people at the business who share a phone. Merge only if it's the same person: the ASIC key enquiry goes out in one
        person's name.
      </p>
      <ul className="mt-3 space-y-2">
        {others.map((o) => (
          <li key={o.id} className="flex flex-wrap items-center gap-2">
            <Link to={`/admin/clients/${o.id}`} className="text-sm font-medium text-zinc-900 underline">{o.fullName || 'Unnamed client'}</Link>
            <Button size="sm" variant="outline" disabled={busy}
              onClick={() => merge.mutate({ from: o.id, into: client.id })}>Merge into this client</Button>
            <Button size="sm" variant="outline" disabled={busy}
              onClick={() => merge.mutate({ from: client.id, into: o.id })}>Merge this into them</Button>
            <Button size="sm" variant="ghost" disabled={busy} onClick={() => dismiss.mutate(o.flagged)}>Different people</Button>
          </li>
        ))}
      </ul>
    </div>
  )
}

/** One card per ABN: who deals with us about it, who gets its reminders, and its business names. */
function BusinessNames({ client }: { client: ClientDetail }) {
  if (client.abns.length === 0) return <EmptyState title="No ABN yet." message="Business names appear once we have the client's ABN and it has been checked against the register." />
  return (
    <div className="space-y-6">
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
    <div className="rounded-lg ring-1 ring-zinc-200">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-100 bg-zinc-50/60 px-4 py-3 rounded-t-lg">
        <div className="min-w-0">
          <div className="font-mono text-sm text-zinc-900 tabular-nums">ABN {formatAbn(abn.abn)}</div>
          <div className="mt-0.5 text-xs text-zinc-500">
            {abn.contacts.length === 0 ? 'Only contact' : (
              <>Also on this ABN:{' '}
                {abn.contacts.map((c, i) => (
                  <span key={c.clientId}>
                    {i > 0 ? ', ' : ''}
                    <Link to={`/admin/clients/${c.clientId}`} className="text-zinc-700 underline">{c.fullName || 'Unnamed client'}</Link>
                    {c.isReminderContact ? ' (gets reminders)' : ''}
                  </span>
                ))}
              </>
            )}
            {' · '}register {abn.registerCheckedAt ? `checked ${relativeTime(abn.registerCheckedAt)}` : 'not checked yet'}
          </div>
        </div>
        <div className="flex items-center gap-2">
          {abn.isReminderContact ? (
            <StatusPill tone="indigo">GETS REMINDERS</StatusPill>
          ) : (
            <>
              <span className="text-xs text-zinc-500">Reminders go to {reminderOther?.fullName || 'another contact'}</span>
              <Button size="sm" variant="outline" disabled={makeReminder.isPending} onClick={() => makeReminder.mutate(client.id)}>
                Send them to {client.fullName.split(' ')[0] || 'this client'}
              </Button>
            </>
          )}
        </div>
      </div>

      {abn.businessNames.length === 0 ? (
        <div className="px-4 py-3 text-sm text-zinc-400">No business names on the register for this ABN.</div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 border-b border-zinc-100">
                <th className="py-2 px-4">Name</th>
                <th className="py-2 pr-4">Due</th>
                <th className="py-2 pr-4">Last renewed</th>
                <th className="py-2 pr-4">ASIC key</th>
                <th className="py-2 pr-4">Status</th>
                <th className="py-2 pr-4" />
              </tr>
            </thead>
            <tbody>
              {abn.businessNames.map((n) => (
                <tr key={n.id} className={`border-b border-zinc-50 last:border-0 align-top ${n.status === 'Active' ? '' : 'text-zinc-400'}`}>
                  <td className="py-2 px-4">
                    <div className={n.status === 'Active' ? 'text-zinc-900' : ''}>{n.name}</div>
                    <div className="text-xxs font-mono text-zinc-400">
                      {n.registrationDate ? `reg ${fmtDate(n.registrationDate)}` : 'reg date unknown'}{n.asicAccountNumber ? ` · acct ${n.asicAccountNumber}` : ''} · via {n.source}
                    </div>
                  </td>
                  <td className="py-2 pr-4 whitespace-nowrap">
                    {n.renewalDueDate ? (
                      <>
                        <div className="font-mono tabular-nums">{fmtDate(n.renewalDueDate)}</div>
                        {n.dueDateSource ? <div className="text-xxs text-zinc-400">from {n.dueDateSource === 'Renewal' ? 'our renewal' : n.dueDateSource}</div> : null}
                      </>
                    ) : (
                      <span className="text-xs text-zinc-400" title="We haven't renewed it and have no due date on file; renewals fall on the registration anniversary.">{anniversary(n.registrationDate)}</span>
                    )}
                  </td>
                  <td className="py-2 pr-4 font-mono tabular-nums whitespace-nowrap">
                    {n.lastRenewedAt ? `${fmtDate(n.lastRenewedAt)} · ${n.lastRenewalYears} yr` : '—'}
                  </td>
                  <td className="py-2 pr-4 font-mono">{n.asicKey ?? '—'}</td>
                  <td className="py-2 pr-4"><Status kind="businessName" status={n.status} /></td>
                  <td className="py-2 pr-4 text-right whitespace-nowrap">
                    <NameMenu name={n} busy={setStatus.isPending} onStatus={(status) => setStatus.mutate({ id: n.id, status })} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

/** Exclude / include, and copying the ASIC key. */
function NameMenu({ name, busy, onStatus }: { name: ClientBusinessNameDto; busy: boolean; onStatus: (s: ClientBusinessNameDto['status']) => void }) {
  const copyKey = () => {
    if (!name.asicKey) return
    void navigator.clipboard.writeText(name.asicKey).then(() => sileo.success({ title: 'ASIC key copied' }))
  }
  if (name.status === 'Cancelled' && !name.asicKey) return null
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button size="sm" variant="ghost" className="h-7 w-7 p-0" aria-label={`Actions for ${name.name}`} disabled={busy}>
          <MoreHorizontal className="h-4 w-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {name.asicKey ? <DropdownMenuItem onSelect={copyKey}><Copy className="h-4 w-4" /> Copy ASIC key</DropdownMenuItem> : null}
        {name.status === 'Active' ? <DropdownMenuItem onSelect={() => onStatus('Excluded')}>Leave out of reminders</DropdownMenuItem> : null}
        {name.status === 'Excluded' ? <DropdownMenuItem onSelect={() => onStatus('Active')}>Include in reminders again</DropdownMenuItem> : null}
      </DropdownMenuContent>
    </DropdownMenu>
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
      <textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={5} maxLength={2000}
        className="w-full rounded-md ring-1 ring-zinc-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500" />
      <div className="mt-2 flex justify-end">
        <Button size="sm" disabled={save.isPending || notes === (client.notes ?? '')} onClick={() => save.mutate()}>Save notes</Button>
      </div>
    </div>
  )
}

function Section({ kicker, title, children, className = 'mt-6' }: { kicker: string; title: string; children: ReactNode; className?: string }) {
  return (
    <div className={`rounded-xl bg-white p-5 ring-1 ring-zinc-200 shadow-sm ${className}`}>
      <div className="mb-4">
        <KickerLabel>{kicker}</KickerLabel>
        <h2 className="mt-0.5 text-base font-semibold text-zinc-900 tracking-tight">{title}</h2>
      </div>
      {children}
    </div>
  )
}

function Row({ label, value, mono }: { label: string; value: ReactNode; mono?: boolean }) {
  return (
    <div className="grid grid-cols-[8rem_1fr] gap-3 items-baseline">
      <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">{label}</dt>
      <dd className={`text-sm text-zinc-900 ${mono ? 'font-mono tabular-nums' : ''}`}>{value}</dd>
    </div>
  )
}
