import { Fragment, useMemo, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, ChevronLeft, Loader2, TriangleAlert } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type ClientDetail } from '../api/client'
import { EmptyState, PageHeader } from './_ui'
import { fmtDate, formatAbn, relativeTime } from './_utils'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { sample } from '@/components/skeleton-samples'
import { Skeletonizer } from '@/components/skeletonizer'
import { cn } from '@/lib/utils'

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
    <div className="mx-auto max-w-5xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <Button variant="ghost" size="sm" asChild className="-ml-2 mb-2 text-muted-foreground">
        <Link to="/admin/clients"><ChevronLeft /> Clients</Link>
      </Button>
      <PageHeader
        title="Possible duplicates"
        subtitle="Same ABN and name or mobile, under a different email."
        right={queue.length ? <span className="text-sm text-muted-foreground tabular-nums">{remaining.length} left{skipped.length ? ` · ${skipped.length} skipped` : ''}</span> : null}
      />

      {isPending ? (
        <PairSkeleton />
      ) : !current ? (
        <div className="space-y-3">
          <EmptyState
            title={queue.length ? 'You skipped the rest' : 'All clear'}
            message={queue.length ? undefined : 'Every possible duplicate has been decided.'}
          />
          {skipped.length ? <div className="text-center"><Button variant="outline" size="sm" onClick={() => setSkipped([])}>Go through the skipped ones</Button></div> : null}
        </div>
      ) : !a.data || !b.data ? (
        <PairSkeleton />
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

/** The pair, drawn from sample clients as a skeleton. */
function PairSkeleton() {
  return (
    <Skeletonizer loading>
      <Pair a={SKELETON_PAIR[0]} b={SKELETON_PAIR[1]} busy={false} onMerge={() => {}} onApart={() => {}} onSkip={() => {}} />
    </Skeletonizer>
  )
}

/** A sample client, for drawing the pair as a skeleton while the real ones load. */
function sampleClient(i: number): ClientDetail {
  const abn = sample.abn(i)
  return {
    id: sample.id(i), fullName: sample.name(i), mobileNumber: sample.mobile(), dateOfBirth: '1984-06-12', address: '12 Harbour Street, Sydney NSW 2000',
    hasTfn: false, hasPurchased: i === 0, reminderOptIn: true, unsubscribedAt: null, isTest: false, notes: null,
    createdAt: sample.daysAgo(400), lastSeenAt: sample.daysAgo(i * 20 + 3), possibleDuplicateOf: null, duplicates: [],
    emails: [{ id: sample.id(i), email: sample.email(i), isPrimary: true, ontraportContactId: null, source: 'Ontraport' }],
    abns: [{
      abn, isReminderContact: true, registerCheckedAt: null, contacts: [],
      businessNames: [0, 1].map((n) => ({
        id: sample.id(i * 2 + n), name: sample.business(i * 2 + n), abn, registrationDate: null, asicAccountNumber: null, renewalDueDate: null,
        dueDateSource: null, asicKey: null, asicKeyReceivedAt: null, lastRenewedAt: null, lastRenewalYears: null, status: 'Active', source: 'Register',
        portalLinkedAt: null, portalLink: null,
      })),
    }],
    sales: [], leads: [], renewals: [], keyRequests: [], messages: [],
  }
}
const SKELETON_PAIR = [sampleClient(0), sampleClient(1)] as const

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
    <div className="space-y-4">
      <Card className="gap-0 overflow-hidden py-0">
        <Table>
          <TableHeader>
            <TableRow className="hover:bg-transparent">
              <TableHead className="hidden w-36 px-4 sm:table-cell" />
              {[a, b].map((c) => (
                <TableHead key={c.id} className="h-auto w-1/2 px-3 py-3 align-top whitespace-normal sm:w-auto sm:px-4">
                  <Link to={`/admin/clients/${c.id}`} className="text-heading text-link hover:underline">{c.fullName || 'Unnamed client'}</Link>
                  <div className="mt-1 flex flex-wrap gap-1">
                    {c.hasPurchased ? <Badge variant="secondary">Customer</Badge> : <Badge variant="outline">Lead</Badge>}
                    {c === keep ? <Badge variant="outline"><span className="sm:hidden">Kept</span><span className="hidden sm:inline">Kept if merged</span></Badge> : null}
                  </div>
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((r) => {
              const label = (
                <>
                  <span className="text-caption text-muted-foreground">{r.label}</span>
                  {r.same ? <span className="ml-2 inline-flex items-center gap-1 text-caption text-muted-foreground sm:mt-0.5 sm:ml-0 sm:flex"><Check className="size-3" /> Same</span> : null}
                </>
              )
              return (
                <Fragment key={r.label}>
                  {/* Phones: the label on its own line, then the two clients side by side under it. */}
                  <TableRow className={cn('border-b-0 sm:hidden', r.same && 'bg-muted/40')}>
                    <TableCell colSpan={2} className="px-3 pt-2.5 pb-0">{label}</TableCell>
                  </TableRow>
                  <TableRow className={cn('align-top', r.same && 'bg-muted/40')}>
                    <TableCell className="hidden px-4 py-2.5 align-top sm:table-cell"><div className="flex flex-col">{label}</div></TableCell>
                    <TableCell className="px-3 py-2.5 align-top whitespace-normal [overflow-wrap:anywhere] sm:px-4">{r.a}</TableCell>
                    <TableCell className="px-3 py-2.5 align-top whitespace-normal [overflow-wrap:anywhere] sm:px-4">{r.b}</TableCell>
                  </TableRow>
                </Fragment>
              )
            })}
          </TableBody>
        </Table>
      </Card>

      <Alert>
        <TriangleAlert />
        <AlertDescription title={MERGE_WARNING}>
          <span className="hidden sm:inline">Merging deletes the other client and can’t be undone.</span>
          {/* Phones can't hover: the whole warning, in place of the short one. */}
          <span className="sm:hidden">{MERGE_WARNING} It can’t be undone.</span>
        </AlertDescription>
      </Alert>

      <div className="flex flex-wrap items-center gap-2">
        <Button className="w-full sm:w-auto" disabled={busy} onClick={() => onMerge(other.id, keep.id)}>Same person: merge into {firstName(keep)}</Button>
        <Button className="w-full sm:w-auto" variant="outline" disabled={busy} onClick={() => onMerge(keep.id, other.id)}>Merge into {firstName(other)} instead</Button>
        <Button className="w-full sm:w-auto" variant="outline" disabled={busy} onClick={onApart}>Different people</Button>
        <Button className="w-full sm:w-auto" variant="ghost" disabled={busy} onClick={onSkip}>Skip for now</Button>
        {busy ? <Loader2 className="size-4 animate-spin text-muted-foreground" /> : null}
      </div>
    </div>
  )
}

const MERGE_WARNING = 'Merging moves the emails, ABNs and history onto the kept client and deletes the other. The ASIC key enquiry goes out in one person’s name, so merge only when you’re sure.'

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
    ? <ul className={cn('space-y-0.5', mono && 'font-mono tabular-nums')}>{items.map((i) => <li key={i} className="break-all">{i}</li>)}</ul>
    : <span className="text-muted-foreground">—</span>
  const text = (v: string | null | undefined, mono = false) => v ? <span className={cn(mono && 'font-mono tabular-nums')}>{v}</span> : <span className="text-muted-foreground">—</span>
  const history = (c: ClientDetail) =>
    `${c.renewals.length} renewal${c.renewals.length === 1 ? '' : 's'} · ${c.sales.length} sale${c.sales.length === 1 ? '' : 's'} · ${c.leads.length} wizard visit${c.leads.length === 1 ? '' : 's'}`

  return [
    { label: 'Name', a: text(a.fullName), b: text(b.fullName), same: !!norm(a.fullName) && norm(a.fullName) === norm(b.fullName) },
    { label: 'Mobile', a: text(a.mobileNumber), b: text(b.mobileNumber), same: mobileKey(a.mobileNumber).length === 9 && mobileKey(a.mobileNumber) === mobileKey(b.mobileNumber) },
    { label: 'Date of birth', a: text(a.dateOfBirth ? fmtDate(a.dateOfBirth) : null), b: text(b.dateOfBirth ? fmtDate(b.dateOfBirth) : null), same: !!a.dateOfBirth && a.dateOfBirth === b.dateOfBirth },
    { label: 'Emails', a: list(emails(a)), b: list(emails(b)), same: overlap(emails(a), emails(b)) },
    { label: 'Address', a: text(a.address), b: text(b.address), same: !!norm(a.address) && norm(a.address) === norm(b.address) },
    { label: 'ABNs', a: list(abns(a).map(formatAbn), true), b: list(abns(b).map(formatAbn), true), same: overlap(abns(a), abns(b)) },
    { label: 'Business names', a: list(names(a)), b: list(names(b)), same: false },
    { label: 'History', a: text(history(a)), b: text(history(b)), same: false },
    { label: 'Last seen', a: text(relativeTime(a.lastSeenAt)), b: text(relativeTime(b.lastSeenAt)), same: false },
  ]
}
