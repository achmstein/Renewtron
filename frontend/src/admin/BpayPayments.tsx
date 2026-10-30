import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { RotateCw, Send } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type BpayBill } from '../api/client'
import { EmptyState, PageHeader, RefreshButton, StatTile, StatusPill, type Tone } from './_ui'
import { fmtMoney2, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { Button } from '@/components/ui/button'
import { Dialog, DialogBody, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'

const STATUS: Record<string, { label: string; tone: Tone }> = {
  UNKNOWN:           { label: 'No answer — check', tone: 'amber' },
  REJECTED:          { label: 'Refused',           tone: 'red' },
  IN_APPROVAL:       { label: 'Awaiting approval', tone: 'indigo' },
  SCHEDULED:         { label: 'Scheduled',         tone: 'indigo' },
  OVERDUE:           { label: 'Overdue',           tone: 'amber' },
  PROCESSING:        { label: 'Processing',        tone: 'indigo' },
  SENT:              { label: 'Sent',              tone: 'indigo' },
  PAID:              { label: 'Paid',              tone: 'emerald' },
  FAILED:            { label: 'Failed',            tone: 'red' },
  CANCELLED:         { label: 'Cancelled',         tone: 'red' },
  APPROVAL_REJECTED: { label: 'Approval rejected', tone: 'red' },
  APPROVAL_RECALLED: { label: 'Approval recalled', tone: 'red' },
  APPROVAL_BLOCKED:  { label: 'Approval blocked',  tone: 'red' },
}

export default function BpayPayments() {
  const [showPaid, setShowPaid] = useState(false)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [confirming, setConfirming] = useState(false)

  const queryClient = useQueryClient()
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['admin-bpay', { showPaid }],
    queryFn: () => api.admin.bpayBills(showPaid),
  })
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin-bpay'] })

  const items = data?.items ?? []
  const payable = items.filter((b) => b.payable && b.amount != null)
  const chosen = payable.filter((b) => selected.has(b.id))
  const total = chosen.reduce((sum, b) => sum + (b.amount ?? 0), 0)
  const inFlight = items.filter((b) => !b.paidAt && b.transferStatus && !b.payable)
  const toPay = items.filter((b) => b.payable)
  const missingFee = toPay.some((b) => b.amount == null)

  const toggle = (id: string) => setSelected((s) => {
    const next = new Set(s)
    if (next.has(id)) next.delete(id); else next.add(id)
    return next
  })
  const allChosen = payable.length > 0 && chosen.length === payable.length
  const toggleAll = () => setSelected(allChosen ? new Set() : new Set(payable.map((b) => b.id)))

  const payMutation = useMutation({
    mutationFn: () => api.admin.payBpayBills(chosen.map((b) => b.id), Math.round(total * 100) / 100),
    onSettled: () => { setSelected(new Set()); void invalidate() },
  })
  const pay = () => {
    void sileo.promise(payMutation.mutateAsync(), {
      loading: { title: `Paying ${chosen.length} BPAY ${chosen.length === 1 ? 'bill' : 'bills'} through Airwallex…` },
      success: (r) => {
        const failed = r.outcomes.filter((o) => !o.sent).length
        return failed === 0
          ? { title: `${r.outcomes.length} sent to Airwallex`, description: 'Each is marked paid once Airwallex reports it paid.' }
          : { title: `${r.outcomes.length - failed} sent, ${failed} not sent`, description: 'See the rows marked Refused or No answer.' }
      },
      error: (e) => ({ title: 'Nothing was paid', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {}).finally(() => setConfirming(false))
  }

  const syncMutation = useMutation({ mutationFn: () => api.admin.syncBpayPayments(), onSettled: () => void invalidate() })
  const sync = () => {
    void sileo.promise(syncMutation.mutateAsync(), {
      loading: { title: 'Checking Airwallex…' },
      success: (r) => ({ title: r.changed === 0 ? 'No changes' : `${r.changed} updated` }),
      error: (e) => ({ title: 'Could not check Airwallex', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const columns = useMemo<DataTableColumn<BpayBill>[]>(() => [
    {
      id: 'select',
      header: () => (
        <input type="checkbox" aria-label="Select all" checked={allChosen} disabled={payable.length === 0} onChange={toggleAll} />
      ),
      enableSorting: false,
      meta: { className: 'w-8' },
      cell: ({ row }) => {
        const b = row.original
        const can = b.payable && b.amount != null
        return (
          <input
            type="checkbox"
            aria-label={`Select ${b.businessName ?? ''}`}
            disabled={!can}
            checked={selected.has(b.id)}
            onClick={(e) => e.stopPropagation()}
            onChange={() => toggle(b.id)}
          />
        )
      },
    },
    {
      id: 'business',
      accessorFn: (b) => b.businessName,
      header: 'Business',
      meta: { className: 'min-w-[12rem] max-w-[18rem]' },
      cell: ({ row }) => {
        const b = row.original
        return (
          <div>
            <Link to={`/admin/renewals/${b.id}`} className="text-sm font-medium text-zinc-900 hover:underline truncate block">{b.businessName}</Link>
            <div className="text-xxs font-mono tabular-nums text-zinc-500">{b.abn} · {b.renewalYears} {b.renewalYears === 1 ? 'year' : 'years'}</div>
          </div>
        )
      },
    },
    {
      id: 'bpay',
      header: 'BPAY',
      enableSorting: false,
      cell: ({ row }) => (
        <div className="text-xs font-mono tabular-nums text-zinc-700">
          <div>Biller {row.original.billerCode}</div>
          <div className="text-zinc-500">Ref {row.original.customerReference}</div>
        </div>
      ),
    },
    {
      id: 'amount',
      accessorFn: (b) => b.amount ?? -1,
      header: 'Amount',
      meta: { className: 'text-right', headerClassName: 'text-right' },
      cell: ({ row }) => row.original.amount == null
        ? <span className="text-xs text-amber-700">fee not set</span>
        : <span className="text-sm font-mono tabular-nums text-zinc-900">{fmtMoney2(row.original.amount)}</span>,
    },
    {
      id: 'status',
      header: 'Payment',
      enableSorting: false,
      cell: ({ row }) => {
        const b = row.original
        const s = b.transferStatus ? STATUS[b.transferStatus] ?? { label: b.transferStatus, tone: 'zinc' as Tone } : null
        return (
          <div className="max-w-[16rem]">
            {b.paidAt ? <StatusPill tone="emerald">PAID {relativeTime(b.paidAt)}</StatusPill>
              : s ? <StatusPill tone={s.tone}>{s.label}</StatusPill>
              : <StatusPill tone="amber">To pay</StatusPill>}
            {b.error ? <div className="mt-1 text-xxs font-mono text-red-700 break-words">{b.error}</div> : null}
            {b.transferId ? <div className="mt-1 text-xxs font-mono text-zinc-400">{b.transferId}</div> : null}
          </div>
        )
      },
    },
    {
      id: 'lodged',
      accessorFn: (b) => b.lodgedAt,
      header: 'Lodged',
      cell: ({ row }) => (
        <div>
          <div className="text-xs text-zinc-700 tabular-nums">{relativeTime(row.original.lodgedAt)}</div>
          {row.original.transactionReference ? <div className="text-xxs font-mono text-zinc-400">{row.original.transactionReference}</div> : null}
        </div>
      ),
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [selected, allChosen, payable.length])

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="PIPELINE"
        title="BPAY to pay"
        subtitle="Renewals lodged with ASIC to pay later by BPAY. Tick the bills, check the total, and pay them through Airwallex — one payment per bill. Each renewal is marked paid when Airwallex reports it paid."
        right={
          <>
            <button
              onClick={sync}
              disabled={syncMutation.isPending}
              className="inline-flex items-center gap-2 whitespace-nowrap rounded-md bg-white text-zinc-800 px-3 py-2 text-sm font-medium ring-1 ring-inset ring-zinc-300 hover:bg-zinc-50 shadow-sm disabled:opacity-50 transition"
              title="Read the latest status of payments in flight (also runs every 10 minutes)"
            >
              <RotateCw className={`h-4 w-4 ${syncMutation.isPending ? 'animate-spin' : ''}`} />
              Check Airwallex
            </button>
            <RefreshButton onClick={() => void refetch()} busy={isFetching} />
          </>
        }
      />

      {data && !data.airwallexReady ? (
        <div className="mb-6 rounded-lg bg-amber-50 p-4 text-sm text-amber-800 ring-1 ring-amber-200">
          Airwallex isn't set up. Add the client ID and API key in <Link to="/admin/settings" className="underline">Settings → Airwallex</Link>.
        </div>
      ) : null}
      {data?.airwallexReady && data.sandbox ? (
        <div className="mb-6 rounded-lg bg-indigo-50 p-4 text-sm text-indigo-800 ring-1 ring-indigo-200">
          Airwallex is in <strong>sandbox</strong> mode: payments are simulated and ASIC is not paid.
        </div>
      ) : null}
      {missingFee ? (
        <div className="mb-6 rounded-lg bg-amber-50 p-4 text-sm text-amber-800 ring-1 ring-amber-200">
          Some bills have no amount because ASIC's fee for that period isn't set. Add it in <Link to="/admin/settings" className="underline">Settings → ASIC credentials</Link>.
        </div>
      ) : null}

      <div className="grid grid-cols-2 sm:grid-cols-3 gap-4">
        <StatTile kicker="TO PAY" label="Bills not yet paid" value={toPay.length.toLocaleString()}
          sub={toPay.length > 0 ? fmtMoney2(toPay.reduce((s, b) => s + (b.amount ?? 0), 0)) + ' at ASIC’s fees' : 'nothing owed'} tone={toPay.length > 0 ? 'amber' : 'zinc'} />
        <StatTile kicker="IN FLIGHT" label="Sent, waiting on Airwallex" value={inFlight.length.toLocaleString()} tone="indigo" />
        <StatTile kicker="SELECTED" label="Ticked to pay now" value={fmtMoney2(total)} sub={`${chosen.length} ${chosen.length === 1 ? 'bill' : 'bills'}`} />
      </div>

      <div className="mt-6 flex flex-wrap items-center justify-between gap-3">
        <label className="flex items-center gap-2 text-sm text-zinc-600">
          <input type="checkbox" checked={showPaid} onChange={(e) => setShowPaid(e.target.checked)} />
          Show bills paid in the last 30 days
        </label>
        <Button onClick={() => setConfirming(true)} disabled={chosen.length === 0 || !data?.airwallexReady || payMutation.isPending}>
          <Send className="h-4 w-4" /> Pay {chosen.length || ''} {chosen.length === 1 ? 'bill' : 'bills'}{chosen.length ? ` · ${fmtMoney2(total)}` : ''}
        </Button>
      </div>

      <div className="mt-4">
        <DataTable
          columns={columns}
          data={items}
          empty={<EmptyState title="No BPAY bills to pay" message="Renewals lodged with ASIC to pay later by BPAY show up here." />}
        />
      </div>

      <Dialog open={confirming} onOpenChange={(o) => { if (!o && !payMutation.isPending) setConfirming(false) }}>
        <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-lg">
          <DialogHeader>
            <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-indigo-700">
              {data?.sandbox ? 'SANDBOX — NO MONEY MOVES' : 'PAY ASIC THROUGH AIRWALLEX'}
            </div>
            <DialogTitle>Pay {chosen.length} BPAY {chosen.length === 1 ? 'bill' : 'bills'}, {fmtMoney2(total)}</DialogTitle>
          </DialogHeader>
          <DialogBody className="space-y-2">
            <ul className="divide-y divide-zinc-100 text-sm">
              {chosen.map((b) => (
                <li key={b.id} className="flex items-center justify-between gap-3 py-1.5">
                  <span className="truncate">{b.businessName} <span className="text-xxs font-mono text-zinc-400">ref {b.customerReference}</span></span>
                  <span className="font-mono tabular-nums">{fmtMoney2(b.amount ?? 0)}</span>
                </li>
              ))}
            </ul>
            <p className="text-xs text-zinc-500">
              {data?.sandbox
                ? 'Each bill becomes one simulated Airwallex payment.'
                : 'Each bill becomes one Airwallex payment from your AUD balance to ASIC (biller code and reference as shown). This can’t be undone from here.'}
            </p>
          </DialogBody>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirming(false)} disabled={payMutation.isPending}>Cancel</Button>
            <Button onClick={pay} disabled={payMutation.isPending} className="bg-indigo-600 hover:bg-indigo-700">
              {payMutation.isPending ? 'Paying…' : `Pay ${fmtMoney2(total)}`}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
