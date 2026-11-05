import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Loader2, TriangleAlert } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type TodayGroup, type TodayItem } from '../api/client'
import { ClientName, DueIn, Queue, SentenceRow, When } from './_rows'
import { statusOf } from './_status'
import { RefreshButton } from './_ui'
import { fmtMoney0, fmtMoney2 } from './_utils'
import { Button } from '@/components/ui/button'

/*
 * Today: the admin's home page. Every queue that needs a person, grouped by job and ordered by
 * risk (the server orders them). Each row says what happened in words, names the client, and
 * carries the next step. The numbers dashboard is under Reports › Overview.
 */

const GROUP_TITLES: Record<TodayGroup['key'], string> = {
  paid: 'Paid, not renewed yet',
  bpay: 'ASIC bills to pay',
  keys: 'ASIC keys',
  decide: 'Decide',
}

export default function Today() {
  const { data, error, isFetching, refetch } = useQuery({
    queryKey: ['admin-today'],
    queryFn: api.admin.today,
    refetchInterval: 120_000,
  })

  const now = new Date()
  const paid = data?.groups.find((g) => g.key === 'paid')

  return (
    <div className="mx-auto max-w-5xl px-4 py-8 sm:px-6 lg:px-8">
      <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
        <div>
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">
            {now.toLocaleDateString('en-AU', { weekday: 'long', day: 'numeric', month: 'long' })}
          </div>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight text-zinc-900">
            {!data ? 'Today' : data.total === 0 ? 'Nothing needs a person right now' : `${data.total.toLocaleString()} thing${data.total === 1 ? '' : 's'} need${data.total === 1 ? 's' : ''} a person`}
          </h1>
          {paid && paid.total ? (
            <p className="mt-1 text-sm text-zinc-500">{fmtMoney0(paid.total)} that clients paid is waiting on us.</p>
          ) : null}
        </div>
        <RefreshButton onClick={() => void refetch()} busy={isFetching} />
      </div>

      {error ? (
        <div className="rounded-lg bg-red-50 p-4 text-sm text-red-700 ring-1 ring-red-200">Couldn’t load today’s work: {error.message}</div>
      ) : !data ? (
        <div className="flex items-center gap-2 text-sm text-zinc-500"><Loader2 className="h-4 w-4 animate-spin" /> Gathering today’s work…</div>
      ) : (
        <div className="space-y-5">
          {data.otpProblem ? (
            <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-red-50 px-4 py-3 ring-1 ring-red-200">
              <div className="flex items-start gap-2.5 text-sm text-red-800">
                <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" />
                <div>
                  <strong className="font-semibold">Card renewals are on hold.</strong> Renewtron can’t read ASIC’s one-time codes from Ontraport, so no card renewal can finish.
                  <div className="mt-0.5 text-xs text-red-700">{data.otpProblem}</div>
                </div>
              </div>
              <Button asChild size="sm" className="bg-red-700 hover:bg-red-800"><Link to="/admin/settings">Fix in Settings</Link></Button>
            </div>
          ) : null}
          {data.groups.map((g) => <Group key={g.key} group={g} />)}
        </div>
      )}
    </div>
  )
}

function Group({ group }: { group: TodayGroup }) {
  const hidden = group.count - group.items.length
  return (
    <Queue
      title={GROUP_TITLES[group.key]}
      count={group.count}
      total={group.total ? fmtMoney0(group.total) : undefined}
      action={<GroupActions group={group} />}
      empty={EMPTY[group.key]}
    >
      {group.items.map((item) => <Row key={`${item.kind}-${item.id}`} item={item} />)}
      {hidden > 0 ? (
        <div className="border-t border-zinc-100 px-4 py-2 text-xs text-zinc-500">
          {group.items.length} of {group.count.toLocaleString()} shown, most urgent first. The rest are in the lists linked above.
        </div>
      ) : null}
    </Queue>
  )
}

const EMPTY: Record<TodayGroup['key'], string> = {
  paid: 'Every paid renewal is done or on its way.',
  bpay: 'No ASIC bills waiting.',
  keys: 'No key requests to send and no key letters to sort.',
  decide: 'Nothing to decide.',
}

function LinkButton({ to, children, primary }: { to: string; children: ReactNode; primary?: boolean }) {
  return (
    <Button asChild size="xs" variant={primary ? 'default' : 'outline'}>
      <Link to={to}>{children}</Link>
    </Button>
  )
}

function GroupActions({ group }: { group: TodayGroup }) {
  switch (group.key) {
    case 'paid':
      return (
        <div className="flex gap-1.5">
          <LinkButton to="/admin/renewals?view=to-fix">Renewals to fix</LinkButton>
          <LinkButton to="/admin/ontraport-sales?status=RenewalFailed,IneligibleForRenewal">Sales</LinkButton>
        </div>
      )
    case 'bpay':
      return group.count > 0 ? <LinkButton to="/admin/bpay" primary>Pay bills</LinkButton> : null
    case 'keys':
      return (
        <div className="flex gap-1.5">
          <LinkButton to="/admin/asic-key-requests">Key requests</LinkButton>
          <LinkButton to="/admin/asic-keys?view=to-sort">Keys to sort</LinkButton>
        </div>
      )
    case 'decide':
      return group.count > 0 ? <LinkButton to="/admin/clients/duplicates">Review duplicates</LinkButton> : null
  }
}

/* ─────────────── Rows: each kind of work, in words ─────────────── */

function Row({ item }: { item: TodayItem }) {
  switch (item.kind) {
    case 'renewal': return <RenewalRow item={item} />
    case 'sale': return <SaleRow item={item} />
    case 'bpay': return <BpayRow item={item} />
    case 'keyRequests': return <KeyRequestsRow item={item} />
    case 'keyRequest': return <KeyRequestRow item={item} />
    case 'keyLetter': return <KeyLetterRow item={item} />
    case 'duplicate': return <DuplicateRow item={item} />
  }
}

/** The client's name, linked when the record is on a client. */
function Who({ item }: { item: TodayItem }) {
  return <ClientName id={item.clientId} name={item.clientName} />
}

function Name({ children }: { children: ReactNode }) {
  return <span className="font-medium">{children ?? 'a business name'}</span>
}

/** "· $299 · 3 years · due in 9 days" — the facts after the first one, skipping what's missing. */
function Facts({ item, first }: { item: TodayItem; first: ReactNode }) {
  const parts: ReactNode[] = [first]
  if (item.amount != null) parts.push(fmtMoney2(item.amount))
  if (item.years) parts.push(`${item.years} year${item.years === 1 ? '' : 's'}`)
  if (item.dueAt) parts.push(<>due <DueIn at={item.dueAt} /></>)
  return <>{parts.map((p, i) => <span key={i}>{i > 0 ? ' · ' : ''}{p}</span>)}</>
}

/** Why a renewal failed, in words, and what to do about it. */
const RENEWAL_REASONS: Record<string, { what: string; next: string }> = {
  Stuck: { what: 'is stuck', next: 'Nothing has moved for over 2 hours. The reconcile sweep repairs most; open it if it stays.' },
  PaymentRisk: { what: 'failed at the payment step', next: 'ASIC may have taken payment. Check at ASIC before retrying.' },
  AlreadyInProgress: { what: 'is blocked: ASIC has a renewal open for this ABN', next: 'Usually another renewal finishing. Check at ASIC, then retry.' },
  Terminal: { what: 'was refused by ASIC', next: 'The details didn’t pass ASIC’s checks. Open it to see what ASIC said.' },
  Transient: { what: 'failed and gave up retrying', next: 'Network or ASIC errors on every attempt. Retry once ASIC is reachable.' },
}

function RenewalRow({ item }: { item: TodayItem }) {
  const reason = RENEWAL_REASONS[item.reason] ?? { what: 'failed', next: item.failedAtStep ? `Stopped at “${item.failedAtStep}”.` : 'Open it to see what went wrong.' }
  return (
    <SentenceRow
      meaning="action"
      to={`/admin/renewals/${item.id}`}
      what={<><Who item={item} />: renewal of <Name>{item.businessName}</Name> {reason.what}.</>}
      why={<><Facts item={item} first={<When at={item.at} />} /><div className="mt-0.5" title={item.detail ?? undefined}>{reason.next}</div></>}
      actions={item.canRetry ? <RetryRenewal id={item.id} /> : <LinkButton to={`/admin/renewals/${item.id}`}>Open</LinkButton>}
    />
  )
}

function SaleRow({ item }: { item: TodayItem }) {
  const ineligible = item.reason === 'IneligibleForRenewal'
  const to = `/admin/ontraport-sales/${item.id}`
  return (
    <SentenceRow
      meaning="action"
      to={to}
      what={ineligible
        ? <><Who item={item} /> paid for <Name>{item.businessName}</Name>, but it can’t be renewed with us.</>
        : <><Who item={item} /> paid for <Name>{item.businessName}</Name>; the renewal never started.</>}
      why={<>
        <Facts item={item} first={<>Ontraport sale, <When at={item.at} /></>} />
        <div className="mt-0.5" title={item.detail ?? undefined}>
          {ineligible ? 'Decide: refund, or contact the client.' : item.detail ? `Last error: ${truncate(item.detail, 110)}` : 'Open the sale to see why.'}
        </div>
      </>}
      actions={<LinkButton to={to}>Open sale</LinkButton>}
    />
  )
}

function BpayRow({ item }: { item: TodayItem }) {
  const status = statusOf('bpay', item.reason)
  return (
    <SentenceRow
      meaning={status.meaning}
      to={`/admin/renewals/${item.id}`}
      what={<>ASIC bill for <Name>{item.businessName}</Name>{item.clientName ? <> (<Who item={item} />)</> : null}{item.amount != null ? `, ${fmtMoney2(item.amount)}` : ''}.</>}
      why={item.reason === 'TO_PAY'
        ? <>Lodged <When at={item.at} />. ASIC holds the renewal until it’s paid.</>
        : <><span title={item.detail ?? undefined}>Airwallex: {status.label.toLowerCase()}.</span> Lodged <When at={item.at} />.</>}
    />
  )
}

function KeyRequestsRow({ item }: { item: TodayItem }) {
  const n = item.count ?? 0
  return (
    <SentenceRow
      meaning="action"
      to="/admin/asic-key-requests"
      what={<>{n} key request{n === 1 ? '' : 's'} to send to ASIC.</>}
      why={<>The oldest came in <When at={item.at} />. Send them from the key tool on the office PC.</>}
    />
  )
}

function KeyRequestRow({ item }: { item: TodayItem }) {
  return (
    <SentenceRow
      meaning="action"
      to={`/admin/asic-key-requests/${item.id}`}
      what={<>Key request for <Name>{item.businessName}</Name>{item.clientName ? <> (<Who item={item} />)</> : null} couldn’t be sent.</>}
      why={<><When at={item.at} />{item.detail ? ` · ${truncate(item.detail, 120)}` : ''}</>}
      actions={<LinkButton to="/admin/asic-key-requests">Open requests</LinkButton>}
    />
  )
}

function KeyLetterRow({ item }: { item: TodayItem }) {
  const status = statusOf('keyLetter', item.reason)
  return (
    <SentenceRow
      meaning={status.meaning}
      to={`/admin/asic-keys/${item.id}`}
      what={<>Key letter for <Name>{item.businessName}</Name>: {status.label.toLowerCase()}.</>}
      why={<><span>Arrived <When at={item.at} /></span>{status.hint ? ` · ${status.hint}` : ''}</>}
      actions={item.canRetry
        ? <RetryKeyLetter id={item.id} />
        : <LinkButton to="/admin/asic-keys?view=to-sort">Choose client</LinkButton>}
    />
  )
}

function DuplicateRow({ item }: { item: TodayItem }) {
  return (
    <SentenceRow
      meaning="quiet"
      to={`/admin/clients/${item.id}`}
      what={<><Who item={item} /> may be the same person as <ClientName id={item.otherClientId} name={item.otherClientName} />.</>}
      why={<>Same ABN, different email or date of birth · found <When at={item.at} /></>}
      actions={<LinkButton to="/admin/clients/duplicates">Review</LinkButton>}
    />
  )
}

/* ─────────────── Inline actions (the same endpoints the record pages use) ─────────────── */

function useTodayAction<T>(fn: (id: string) => Promise<T>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: ['admin-today'] })
      void queryClient.invalidateQueries({ queryKey: ['admin-sidebar-counts'] })
    },
  })
}

function RetryRenewal({ id }: { id: string }) {
  const m = useTodayAction(api.admin.retryRenewal)
  const go = () => void sileo.promise(m.mutateAsync(id), {
    loading: { title: 'Retrying renewal…' },
    success: (r) => ({ title: r.message ?? 'Renewal queued for retry.' }),
    error: (e) => ({ title: 'Retry refused', description: e instanceof Error ? e.message : undefined }),
  }).catch(() => {})
  return <Button size="xs" onClick={go} disabled={m.isPending}>{m.isPending ? <Loader2 className="animate-spin" /> : null}Retry</Button>
}

function RetryKeyLetter({ id }: { id: string }) {
  const m = useTodayAction(api.admin.retryAsicKey)
  const go = () => void sileo.promise(m.mutateAsync(id), {
    loading: { title: 'Reading the key letter again…' },
    success: (r) => r.status === 'Completed' || r.status === 'SavedToClient'
      ? { title: `Key ${r.asicKey ?? ''} saved` }
      : { title: `Still ${statusOf('keyLetter', r.status).label.toLowerCase()}`, description: r.errorMessage ?? undefined },
    error: (e) => ({ title: 'Retry failed', description: e instanceof Error ? e.message : undefined }),
  }).catch(() => {})
  return <Button size="xs" variant="outline" onClick={go} disabled={m.isPending}>{m.isPending ? <Loader2 className="animate-spin" /> : null}Retry</Button>
}

function truncate(s: string, n: number) {
  return s.length > n ? s.slice(0, n - 1) + '…' : s
}

