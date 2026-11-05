import { useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronLeft, Loader2 } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type ClientBusinessNameDto, type ClientDetail } from '../api/client'
import { EmptyState, KickerLabel, StatusPill, type Tone } from './_ui'
import { fmtDate, fmtMoney2, formatAbn, relativeTime } from './_utils'
import { Button } from '@/components/ui/button'

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

      <div className="rounded-2xl bg-white p-6 ring-1 ring-zinc-200 shadow-sm">
        <div className="flex items-start justify-between gap-4 flex-wrap">
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
      </div>

      <DuplicatePanel client={data} />

      <Section kicker="BUSINESS NAMES" title={`${data.businessNames.filter((n) => n.status === 'Active').length} active`}>
        <BusinessNames client={data} />
      </Section>

      <div className="mt-6 grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Section kicker="PROFILE" title="Contact details">
          <dl className="space-y-2">
            <Row label="Date of birth" value={data.dateOfBirth ? fmtDate(data.dateOfBirth) : '—'} mono />
            <Row label="Address" value={data.address ?? '—'} />
            <Row label="TFN on file" value={data.hasTfn ? 'Yes' : 'No'} />
            <Row label="ABNs" value={data.abns.map((a) => formatAbn(a.abn)).join(', ') || '—'} mono />
            <Row label="Emails" value={
              <ul className="space-y-0.5">
                {data.emails.map((e) => (
                  <li key={e.id} className="font-mono text-xs break-all">
                    {e.email}
                    <span className="text-zinc-400"> · {e.source}{e.ontraportContactId ? ` · Ontraport ${e.ontraportContactId}` : ''}{e.isPrimary ? ' · primary' : ''}</span>
                  </li>
                ))}
              </ul>
            } />
          </dl>
        </Section>
        <Section kicker="NOTES" title="Staff notes">
          <Notes client={data} />
        </Section>
      </div>

      <div className="mt-6 grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Section kicker="HISTORY" title="Renewals">
          <History rows={data.renewals.map((r) => ({
            key: r.id, to: `/admin/renewals/${r.id}`, title: r.businessName,
            sub: `${fmtDate(r.initiatedAt)} · ${r.renewalYears} yr · ${fmtMoney2(r.amount)} · ${r.source}`, status: r.status,
          }))} />
        </Section>
        <Section kicker="HISTORY" title="Ontraport sales">
          <History rows={data.sales.map((s) => ({
            key: s.id, to: undefined, title: s.businessName,
            sub: `${fmtDate(s.syncedAt)} · ${fmtMoney2(s.amountPaid)} · contact ${s.ontraportContactId}${s.renewalDueDate ? ` · due ${fmtDate(s.renewalDueDate)}` : ''}`, status: s.status,
          }))} />
        </Section>
        <Section kicker="HISTORY" title="ASIC key requests">
          <History rows={data.keyRequests.map((k) => ({
            key: k.id, to: undefined, title: k.businessName,
            sub: `${fmtDate(k.createdAt)}${k.asicReferenceNumber ? ` · ref ${k.asicReferenceNumber}` : ''}`, status: k.status,
          }))} />
        </Section>
        <Section kicker="HISTORY" title="Wizard visits">
          <History rows={data.leads.map((l) => ({
            key: l.id, to: `/admin/leads/${l.id}`, title: `${formatAbn(l.abn)} · ${l.email}`, sub: fmtDate(l.createdAt), status: l.outcome,
          }))} />
        </Section>
        {data.messages.length ? (
          <Section kicker="HISTORY" title="Emails sent">
            <History rows={data.messages.map((m) => ({
              key: m.id, to: undefined, title: m.subject || m.template, sub: `${fmtDate(m.createdAt)} · ${m.recipient}`, status: m.status,
            }))} />
          </Section>
        ) : null}
      </div>
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
        Another client is on the same ABN with a different email and date of birth. It may be the same person, or someone else
        at the business (a director and a bookkeeper, say). Merge only if it's the same person: the ASIC key enquiry goes out in
        one person's name.
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

function BusinessNames({ client }: { client: ClientDetail }) {
  const queryClient = useQueryClient()
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['admin-client'] })
  const setStatus = useMutation({
    mutationFn: (v: { id: string; status: ClientBusinessNameDto['status'] }) => api.admin.setClientBusinessNameStatus(v.id, v.status),
    onSuccess: refresh,
    onError: (e) => sileo.error({ title: 'Could not update the name', description: errorText(e) }),
  })
  const move = useMutation({
    mutationFn: (v: { id: string; clientId: string }) => api.admin.moveClientBusinessName(v.id, v.clientId),
    onSuccess: () => { refresh(); sileo.success({ title: 'Name moved' }) },
    onError: (e) => sileo.error({ title: 'Could not move the name', description: errorText(e) }),
  })
  // A name can be handed to a client this one is flagged against (who holds the ABN with them).
  const moveTargets = [...(client.possibleDuplicateOf ? [client.possibleDuplicateOf] : []), ...client.duplicates]

  if (client.businessNames.length === 0) return <EmptyState title="No business names yet." message="They appear once the client's ABN has been checked against the register." />
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="text-left text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 border-b border-zinc-100">
            <th className="py-2 pr-4">Name</th>
            <th className="py-2 pr-4">Due</th>
            <th className="py-2 pr-4">Last renewed</th>
            <th className="py-2 pr-4">ASIC key</th>
            <th className="py-2 pr-4">Status</th>
            <th className="py-2" />
          </tr>
        </thead>
        <tbody>
          {client.businessNames.map((n) => (
            <tr key={n.id} className={`border-b border-zinc-50 align-top ${n.status === 'Active' ? '' : 'text-zinc-400'}`}>
              <td className="py-2 pr-4">
                <div className={n.status === 'Active' ? 'text-zinc-900' : ''}>{n.name}</div>
                <div className="text-xxs font-mono text-zinc-400">
                  {formatAbn(n.abn)}{n.registrationDate ? ` · reg ${n.registrationDate}` : ''}{n.asicAccountNumber ? ` · acct ${n.asicAccountNumber}` : ''} · via {n.source}
                </div>
              </td>
              <td className="py-2 pr-4 font-mono tabular-nums whitespace-nowrap">
                {n.renewalDueDate ? fmtDate(n.renewalDueDate) : <span className="text-zinc-400" title="We haven't renewed it and have no due date on file; renewals fall on the registration anniversary.">{anniversary(n.registrationDate)}</span>}
                {n.dueDateSource ? <div className="text-xxs text-zinc-400">{n.dueDateSource}</div> : null}
              </td>
              <td className="py-2 pr-4 font-mono tabular-nums whitespace-nowrap">
                {n.lastRenewedAt ? `${fmtDate(n.lastRenewedAt)} · ${n.lastRenewalYears} yr` : '—'}
              </td>
              <td className="py-2 pr-4 font-mono">{n.asicKey ?? '—'}</td>
              <td className="py-2 pr-4"><StatusPill tone={nameTone(n.status)}>{n.status.toUpperCase()}</StatusPill></td>
              <td className="py-2 text-right whitespace-nowrap">
                {n.status === 'Excluded' ? (
                  <Button size="sm" variant="ghost" disabled={setStatus.isPending} onClick={() => setStatus.mutate({ id: n.id, status: 'Active' })}>Include</Button>
                ) : n.status === 'Active' ? (
                  <Button size="sm" variant="ghost" disabled={setStatus.isPending} title="Leave this name out of reminders"
                    onClick={() => setStatus.mutate({ id: n.id, status: 'Excluded' })}>Exclude</Button>
                ) : null}
                {moveTargets.map((t) => (
                  <Button key={t.id} size="sm" variant="ghost" disabled={move.isPending}
                    onClick={() => move.mutate({ id: n.id, clientId: t.id })}>Move to {t.fullName.split(' ')[0] || 'them'}</Button>
                ))}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function anniversary(registrationDate: string | null) {
  const m = /^(\d{1,2})\/(\d{1,2})\/\d{4}$/.exec(registrationDate ?? '')
  if (!m) return 'unknown'
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
  return `${Number(m[1])} ${months[Number(m[2]) - 1] ?? ''} (year unknown)`
}

function nameTone(status: string): Tone {
  return status === 'Active' ? 'emerald' : status === 'Cancelled' ? 'red' : 'zinc'
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

function History({ rows }: { rows: Array<{ key: string; to?: string; title: string; sub: string; status: string }> }) {
  if (rows.length === 0) return <div className="text-sm text-zinc-400">None</div>
  return (
    <ul className="divide-y divide-zinc-100">
      {rows.map((r) => (
        <li key={r.key} className="py-2 flex items-start justify-between gap-3">
          <div className="min-w-0">
            {r.to ? <Link to={r.to} className="text-sm text-zinc-900 hover:underline">{r.title}</Link> : <div className="text-sm text-zinc-900">{r.title}</div>}
            <div className="text-xxs font-mono text-zinc-500">{r.sub}</div>
          </div>
          <StatusPill tone="zinc">{r.status.toUpperCase()}</StatusPill>
        </li>
      ))}
    </ul>
  )
}

function Section({ kicker, title, children }: { kicker: string; title: string; children: ReactNode }) {
  return (
    <div className="mt-6 rounded-xl bg-white p-5 ring-1 ring-zinc-200 shadow-sm">
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
