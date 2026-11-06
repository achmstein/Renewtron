import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Loader2 } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type TodayItem } from '../api/client'
import { ClientName, DueIn, SentenceRow, When } from './_rows'
import { inSentence, orderStatus, statusOf } from './_status'
import { fmtMoney2 } from './_utils'
import { Button } from '@/components/ui/button'

/*
 * One line of work, in words: the rows of the Today page and of a client's "Needs attention".
 * Each kind of item (renewal, sale, BPAY bill, key request, key letter, duplicate) says what
 * happened, names the client, and carries the next step.
 */

export function LinkButton({ to, children, primary }: { to: string; children: ReactNode; primary?: boolean }) {
  return (
    <Button asChild size="sm" variant={primary ? 'default' : 'outline'}>
      <Link to={to}>{children}</Link>
    </Button>
  )
}

/* ─────────────── Rows: each kind of work, in words ─────────────── */

/** An item's identity for "mark done": kind, record and status, so a change brings it back. */
export const workKey = (i: TodayItem) => `${i.kind}:${i.id}:${i.reason}`

export function WorkRow({ item }: { item: TodayItem }) {
  switch (item.kind) {
    case 'order': return <OrderRow item={item} />
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
  if (item.dueAt) {
    const soon = new Date(item.dueAt).getTime() - Date.now() < 7 * 86_400_000
    parts.push(<span className={soon ? 'font-medium text-destructive' : undefined}>due <DueIn at={item.dueAt} /></span>)
  }
  return <>{parts.map((p, i) => <span key={i}>{i > 0 ? ' · ' : ''}{p}</span>)}</>
}

/** Why a renewal failed: a few words for the row, and the next step on hover. */
const RENEWAL_REASONS: Record<string, { what: string; next: string }> = {
  Stuck: { what: 'is stuck', next: 'Nothing has moved for over 2 hours. The reconcile sweep repairs most; open it if it stays.' },
  PaymentRisk: { what: 'stopped at payment', next: 'ASIC may have taken payment. Check at ASIC before retrying.' },
  CardDeclined: { what: 'failed: ASIC declined our card', next: 'No money was taken. Retry: it pays by BPAY while the card is declined.' },
  AlreadyInProgress: { what: 'is blocked at ASIC', next: 'ASIC has a renewal open for this ABN. Check at ASIC, then retry.' },
  Terminal: { what: 'was refused by ASIC', next: 'The details didn’t pass ASIC’s checks. Open it to see what ASIC said.' },
  Transient: { what: 'failed', next: 'Network or ASIC errors on every attempt. Retry once ASIC is reachable.' },
}

/** A paid renewal that stopped for a person: the client, the name, and why, in a few words. */
function OrderRow({ item }: { item: TodayItem }) {
  const status = orderStatus('NeedsPerson', item.reason)
  const to = `/admin/renewal-orders/${item.id}`
  return (
    <SentenceRow
      doneKey={workKey(item)}
      meaning="action"
      to={to}
      hint={status.hint}
      what={<span><Who item={item} /> paid for <Name>{item.businessName}</Name>: {inSentence(status.label)}</span>}
      why={<Facts item={item} first={<When at={item.at} />} />}
      actions={item.canRetry ? <RetryOrder id={item.id} /> : <LinkButton to={to}>Open</LinkButton>}
    />
  )
}

function RenewalRow({ item }: { item: TodayItem }) {
  const reason = RENEWAL_REASONS[item.reason] ?? { what: 'failed', next: item.failedAtStep ? `Stopped at “${item.failedAtStep}”.` : 'Open it to see what went wrong.' }
  return (
    <SentenceRow
      doneKey={workKey(item)}
      meaning="action"
      to={`/admin/renewals/${item.id}`}
      hint={reason.next}
      what={<span><Who item={item} />: <Name>{item.businessName}</Name> renewal {reason.what}</span>}
      why={<Facts item={item} first={<When at={item.at} />} />}
      actions={item.canRetry ? <RetryRenewal id={item.id} /> : <LinkButton to={`/admin/renewals/${item.id}`}>Open</LinkButton>}
    />
  )
}

function SaleRow({ item }: { item: TodayItem }) {
  const ineligible = item.reason === 'IneligibleForRenewal'
  const to = `/admin/ontraport-sales/${item.id}`
  return (
    <SentenceRow
      doneKey={workKey(item)}
      meaning="action"
      to={to}
      hint={ineligible ? 'Refund, or contact the client.' : item.detail ?? undefined}
      what={<span>
        <Who item={item} /> paid for <Name>{item.businessName}</Name>{ineligible ? ', can’t renew with us' : ', not renewed yet'}
      </span>}
      why={<Facts item={item} first={<When at={item.at} />} />}
      actions={<LinkButton to={to}>Open sale</LinkButton>}
    />
  )
}

function BpayRow({ item }: { item: TodayItem }) {
  const status = statusOf('bpay', item.reason)
  return (
    <SentenceRow
      doneKey={workKey(item)}
      meaning={status.meaning}
      to={`/admin/renewals/${item.id}`}
      hint={item.reason === 'TO_PAY' ? 'ASIC holds the renewal until it’s paid.' : item.detail ?? undefined}
      what={<span>
        BPAY bill for <Name>{item.businessName}</Name>{item.reason === 'TO_PAY' ? '' : `: ${status.label.toLowerCase()}`}
      </span>}
      why={<>{item.clientName ? <><Who item={item} /> · </> : null}<Facts item={item} first={<>lodged <When at={item.at} /></>} /></>}
    />
  )
}

function KeyRequestsRow({ item }: { item: TodayItem }) {
  const n = item.count ?? 0
  return (
    <SentenceRow
      doneKey={workKey(item)}
      meaning="action"
      to="/admin/asic-key-requests"
      hint="Send them from the key tool on the office PC."
      what={<span>{n} key request{n === 1 ? '' : 's'} to send to ASIC</span>}
      why={<>oldest <When at={item.at} /></>}
    />
  )
}

function KeyRequestRow({ item }: { item: TodayItem }) {
  return (
    <SentenceRow
      doneKey={workKey(item)}
      meaning={statusOf('keyRequest', item.reason).meaning}
      to={`/admin/asic-key-requests/${item.id}`}
      hint={item.detail ?? undefined}
      what={<span>Key request for <Name>{item.businessName}</Name> {item.reason === 'Failed' ? 'couldn’t be sent' : item.reason === 'Manual' ? 'is being sent by hand' : 'is waiting to send'}</span>}
      why={<>{item.clientName ? <><Who item={item} /> · </> : null}<When at={item.at} /></>}
      actions={<LinkButton to="/admin/asic-key-requests">Open requests</LinkButton>}
    />
  )
}

function KeyLetterRow({ item }: { item: TodayItem }) {
  const status = statusOf('keyLetter', item.reason)
  return (
    <SentenceRow
      doneKey={workKey(item)}
      meaning={status.meaning}
      to={`/admin/asic-keys/${item.id}`}
      hint={status.hint}
      what={<span>Key letter for <Name>{item.businessName}</Name>: {status.label.toLowerCase()}</span>}
      why={<>arrived <When at={item.at} /></>}
      actions={item.canRetry
        ? <RetryKeyLetter id={item.id} />
        : <LinkButton to="/admin/asic-keys?view=to-sort">Choose client</LinkButton>}
    />
  )
}

function DuplicateRow({ item }: { item: TodayItem }) {
  return (
    <SentenceRow
      doneKey={workKey(item)}
      meaning="quiet"
      to={`/admin/clients/${item.id}`}
      what={<><Who item={item} /> may be <ClientName id={item.otherClientId} name={item.otherClientName} /></>}
      why={<>same ABN · found <When at={item.at} /></>}
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
      void queryClient.invalidateQueries({ queryKey: ['admin-client-work'] })
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
  return <Button size="sm" onClick={go} disabled={m.isPending}>{m.isPending ? <Loader2 className="animate-spin" /> : null}Retry</Button>
}

function RetryOrder({ id }: { id: string }) {
  const m = useTodayAction(api.admin.retryRenewalOrder)
  const go = () => void sileo.promise(m.mutateAsync(id), {
    loading: { title: 'Trying again…' },
    success: (r) => ({ title: r.message }),
    error: (e) => ({ title: 'Not tried again', description: e instanceof Error ? e.message : undefined }),
  }).catch(() => {})
  return <Button size="sm" onClick={go} disabled={m.isPending}>{m.isPending ? <Loader2 className="animate-spin" /> : null}Try again</Button>
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
  return <Button size="sm" variant="outline" onClick={go} disabled={m.isPending}>{m.isPending ? <Loader2 className="animate-spin" /> : null}Retry</Button>
}
