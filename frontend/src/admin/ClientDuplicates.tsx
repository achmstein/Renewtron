import { useMemo, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, ChevronLeft, Loader2 } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type ClientDetail } from '../api/client'
import { EmptyState, PageHeader, StatusPill } from './_ui'
import { fmtDate, formatAbn, relativeTime } from './_utils'
import { Button } from '@/components/ui/button'

/**
 * Possible duplicates one pair at a time: the flagged client beside the one it may be, with
 * matching details marked, and the decision buttons under them. Each decision moves on to
 * the next pair.
 */
export default function ClientDuplicates() {
  const queryClient = useQueryClient()
  const [skipped, setSkipped] = useState<string[]>([])
  const { data: list, isPending } = useQuery({
    queryKey: ['admin-clients', { filter: 'duplicates', take: 500 }],
    queryFn: () => api.admin.clients({ filter: 'duplicates', take: 500 }),
  })
  const queue = (list?.items ?? []).filter((c) => c.possibleDuplicateOfId)
  const remaining = queue.filter((c) => !skipped.includes(c.id))
  const current = remaining[0]

  const a = useQuery({ queryKey: ['admin-client', current?.possibleDuplicateOfId], queryFn: () => api.admin.client(current!.possibleDuplicateOfId!), enabled: !!current })
  const b = useQuery({ queryKey: ['admin-client', current?.id], queryFn: () => api.admin.client(current!.id), enabled: !!current })

  const done = (title: string) => {
    sileo.success({ title })
    void queryClient.invalidateQueries({ queryKey: ['admin-clients'] })
    void queryClient.invalidateQueries({ queryKey: ['admin-client'] })
    void queryClient.invalidateQueries({ queryKey: ['admin-sidebar-counts'] })
  }
  const merge = useMutation({
    mutationFn: (v: { from: string; into: string }) => api.admin.mergeClient(v.from, v.into),
    onSuccess: () => done('Merged'),
    onError: (e) => sileo.error({ title: 'Merge failed', description: e instanceof Error ? e.message : undefined }),
  })
  const dismiss = useMutation({
    mutationFn: (id: string) => api.admin.dismissClientDuplicate(id),
    onSuccess: () => done('Kept as separate clients'),
    onError: (e) => sileo.error({ title: 'Could not update', description: e instanceof Error ? e.message : undefined }),
  })
  const busy = merge.isPending || dismiss.isPending

  return (
    <div className="mx-auto max-w-6xl px-4 py-8 sm:px-6 lg:px-8">
      <div className="mb-4">
        <Link to="/admin/clients" className="inline-flex items-center gap-1 text-xs font-mono uppercase tracking-[0.14em] text-zinc-500 hover:text-zinc-900">
          <ChevronLeft className="h-3 w-3" /> Back to clients
        </Link>
      </div>
      <PageHeader
        kicker="CLIENTS"
        title="Review possible duplicates"
        subtitle="Same ABN and the same name or mobile, under a different email. Merge if it's one person; keep them apart if it's two people at the business."
        right={queue.length ? <span className="text-sm font-mono tabular-nums text-zinc-500">{remaining.length} left{skipped.length ? ` · ${skipped.length} skipped` : ''}</span> : null}
      />

      {isPending ? (
        <div className="py-12 text-center"><Loader2 className="mx-auto h-8 w-8 animate-spin text-brand-600" /></div>
      ) : !current ? (
        <div className="mt-6">
          <EmptyState
            title={queue.length ? 'You skipped the rest.' : 'Nothing to review.'}
            message={queue.length ? undefined : 'Every possible duplicate has been decided.'}
          />
          {skipped.length ? <div className="mt-3 text-center"><Button variant="outline" size="sm" onClick={() => setSkipped([])}>Go through the skipped ones</Button></div> : null}
        </div>
      ) : !a.data || !b.data ? (
        <div className="py-12 text-center"><Loader2 className="mx-auto h-8 w-8 animate-spin text-brand-600" /></div>
      ) : (
        <Pair
          key={current.id}
          a={a.data}
          b={b.data}
          busy={busy}
          onMerge={(from, into) => merge.mutate({ from, into })}
          onApart={() => dismiss.mutate(current.id)}
          onSkip={() => setSkipped((s) => [...s, current.id])}
        />
      )}
    </div>
  )
}

/** Who to keep when merging: the one who has paid us, then the one with more history. */
function weight(c: ClientDetail) {
  return (c.hasPurchased ? 1000 : 0) + c.renewals.length * 10 + c.sales.length * 10 + c.leads.length + c.keyRequests.length
}

function Pair({ a, b, busy, onMerge, onApart, onSkip }: {
  a: ClientDetail
  b: ClientDetail
  busy: boolean
  onMerge: (from: string, into: string) => void
  onApart: () => void
  onSkip: () => void
}) {
  const keep = weight(b) > weight(a) ? b : a
  const other = keep === a ? b : a
  const rows = useMemo(() => compareRows(a, b), [a, b])

  return (
    <div className="mt-6 space-y-4">
      <div className="overflow-x-auto rounded-xl bg-white ring-1 ring-zinc-200 shadow-sm">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-zinc-100 text-left">
              <th className="w-36 px-4 py-3" />
              {[a, b].map((c) => (
                <th key={c.id} className="px-4 py-3 align-top">
                  <Link to={`/admin/clients/${c.id}`} className="text-base font-semibold text-zinc-900 hover:underline">{c.fullName || 'Unnamed client'}</Link>
                  <div className="mt-1 flex flex-wrap gap-1">
                    {c.hasPurchased ? <StatusPill tone="emerald">CUSTOMER</StatusPill> : <StatusPill tone="zinc">LEAD</StatusPill>}
                    {c === keep ? <StatusPill tone="indigo">KEEP IF MERGING</StatusPill> : null}
                  </div>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.label} className="border-b border-zinc-50 last:border-0 align-top">
                <td className="px-4 py-2.5 text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">
                  {r.label}
                  {r.same ? <div className="mt-1 inline-flex items-center gap-1 normal-case tracking-normal text-emerald-700"><Check className="h-3 w-3" /> same</div> : null}
                </td>
                <td className={`px-4 py-2.5 ${r.same ? 'bg-emerald-50/50' : ''}`}>{r.a}</td>
                <td className={`px-4 py-2.5 ${r.same ? 'bg-emerald-50/50' : ''}`}>{r.b}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <Button disabled={busy} onClick={() => onMerge(other.id, keep.id)}>Same person: merge into {firstName(keep)}</Button>
        <Button variant="outline" disabled={busy} onClick={() => onMerge(keep.id, other.id)}>Merge into {firstName(other)} instead</Button>
        <Button variant="outline" disabled={busy} onClick={onApart}>Different people</Button>
        <Button variant="ghost" disabled={busy} onClick={onSkip}>Skip for now</Button>
        {busy ? <Loader2 className="h-4 w-4 animate-spin text-brand-600" /> : null}
      </div>
      <p className="text-xxs text-zinc-500 max-w-3xl">
        Merging moves the emails, ABNs and history onto the kept client and deletes the other. It can't be undone. The ASIC key
        enquiry goes out in one person's name, so merge only when you're sure it's the same person.
      </p>
    </div>
  )
}

function firstName(c: ClientDetail) {
  return c.fullName.split(' ')[0] || 'this one'
}

const norm = (s: string | null | undefined) => (s ?? '').trim().toLowerCase().replace(/\s+/g, ' ')
const mobileKey = (s: string | null | undefined) => (s ?? '').replace(/\D/g, '').slice(-9)

function compareRows(a: ClientDetail, b: ClientDetail): Array<{ label: string; a: ReactNode; b: ReactNode; same: boolean }> {
  const emails = (c: ClientDetail) => c.emails.map((e) => e.email)
  const abns = (c: ClientDetail) => c.abns.map((x) => x.abn)
  const names = (c: ClientDetail) => c.abns.flatMap((x) => x.businessNames).filter((n) => n.status === 'Active').map((n) => n.name)
  const overlap = (x: string[], y: string[]) => x.some((v) => y.includes(v))
  const list = (items: string[], mono = false) => items.length
    ? <ul className={`space-y-0.5 ${mono ? 'font-mono text-xs' : ''}`}>{items.map((i) => <li key={i} className="break-all">{i}</li>)}</ul>
    : <span className="text-zinc-400">—</span>
  const text = (v: string | null | undefined, mono = false) => v ? <span className={mono ? 'font-mono tabular-nums' : ''}>{v}</span> : <span className="text-zinc-400">—</span>
  const history = (c: ClientDetail) =>
    `${c.renewals.length} renewal${c.renewals.length === 1 ? '' : 's'} · ${c.sales.length} sale${c.sales.length === 1 ? '' : 's'} · ${c.leads.length} wizard visit${c.leads.length === 1 ? '' : 's'}`

  return [
    { label: 'Name', a: text(a.fullName), b: text(b.fullName), same: !!norm(a.fullName) && norm(a.fullName) === norm(b.fullName) },
    { label: 'Mobile', a: text(a.mobileNumber, true), b: text(b.mobileNumber, true), same: mobileKey(a.mobileNumber).length === 9 && mobileKey(a.mobileNumber) === mobileKey(b.mobileNumber) },
    { label: 'Date of birth', a: text(a.dateOfBirth ? fmtDate(a.dateOfBirth) : null, true), b: text(b.dateOfBirth ? fmtDate(b.dateOfBirth) : null, true), same: !!a.dateOfBirth && a.dateOfBirth === b.dateOfBirth },
    { label: 'Emails', a: list(emails(a), true), b: list(emails(b), true), same: overlap(emails(a), emails(b)) },
    { label: 'Address', a: text(a.address), b: text(b.address), same: !!norm(a.address) && norm(a.address) === norm(b.address) },
    { label: 'ABNs', a: list(abns(a).map(formatAbn), true), b: list(abns(b).map(formatAbn), true), same: overlap(abns(a), abns(b)) },
    { label: 'Business names', a: list(names(a)), b: list(names(b)), same: false },
    { label: 'History', a: text(history(a)), b: text(history(b)), same: false },
    { label: 'Last seen', a: text(relativeTime(a.lastSeenAt)), b: text(relativeTime(b.lastSeenAt)), same: false },
  ]
}
