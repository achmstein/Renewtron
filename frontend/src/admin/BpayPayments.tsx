import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { Info, RotateCw, Send, TriangleAlert } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type BpayBill } from '../api/client'
import { EmptyState, PageHeader, RefreshButton, Stat, StatStrip } from './_ui'
import { Status } from './_status'
import { fmtMoney2, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { sample } from '@/components/skeleton-samples'
import { sampleRows, Skeletonizer } from '@/components/skeletonizer'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'

const SKELETON_ROWS = sampleRows<BpayBill>(6, (i) => ({
  id: sample.id(i),
  businessName: sample.business(i),
  abn: sample.abn(i),
  renewalYears: i % 3 === 0 ? 3 : 1,
  lodgedAt: sample.daysAgo(i + 1),
  transactionReference: `${4100000 + i * 731}`,
  billerCode: '17301',
  customerReference: `2291${String(880000 + i * 4231)}`,
  amount: i % 3 === 0 ? 245 : 104,
  payable: i % 4 !== 3,
  transferId: null,
  transferStatus: i % 4 === 3 ? 'PROCESSING' : null,
  error: null,
  paidAt: null,
}))

export default function BpayPayments() {
  const [showPaid, setShowPaid] = useState(false)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [confirming, setConfirming] = useState(false)

  const queryClient = useQueryClient()
  const { data, isFetching, isPending, isPlaceholderData, refetch } = useQuery({
    queryKey: ['admin-bpay', { showPaid }],
    queryFn: () => api.admin.bpayBills(showPaid),
  })
  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['admin-bpay'] })
    void queryClient.invalidateQueries({ queryKey: ['admin-sidebar-counts'] })
  }

  const items = data?.items ?? []
  const payable = items.filter((b) => b.payable && b.amount != null)
  const chosen = payable.filter((b) => selected.has(b.id))
  const total = chosen.reduce((sum, b) => sum + (b.amount ?? 0), 0)
  // While the first load is out, the strip is drawn from the sample rows as a skeleton.
  const shown = data ? items : isPending ? SKELETON_ROWS : []
  const inFlight = shown.filter((b) => !b.paidAt && b.transferStatus && !b.payable)
  const toPay = shown.filter((b) => b.payable)
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
      // The tick box sits in the business cell so both stay pinned while the table scrolls on a phone.
      id: 'business',
      accessorFn: (b) => b.businessName,
      header: () => (
        <span className="flex items-center gap-3">
          <Checkbox aria-label="Select all" checked={allChosen ? true : chosen.length > 0 ? 'indeterminate' : false} disabled={payable.length === 0} onCheckedChange={() => toggleAll()} />
          Business
        </span>
      ),
      meta: { sticky: true, className: 'min-w-40 max-w-[45vw] sm:min-w-56 sm:max-w-80' },
      cell: ({ row }) => {
        const b = row.original
        const can = b.payable && b.amount != null
        return (
          <div className="flex min-w-0 items-start gap-3">
            <Checkbox
              className="mt-0.5"
              aria-label={`Select ${b.businessName ?? ''}`}
              disabled={!can}
              checked={selected.has(b.id)}
              onClick={(e) => e.stopPropagation()}
              onCheckedChange={() => toggle(b.id)}
            />
            <div className="min-w-0">
              <Link to={`/admin/renewals/${b.id}`} className="block truncate text-sm font-medium hover:text-link hover:underline">{b.businessName}</Link>
              <div className="truncate text-caption text-muted-foreground">
                <span className="font-mono tabular-nums">{b.abn}</span> · {b.renewalYears} {b.renewalYears === 1 ? 'year' : 'years'}
              </div>
            </div>
          </div>
        )
      },
    },
    {
      id: 'bpay',
      header: 'BPAY',
      enableSorting: false,
      meta: { hideBelow: 'md' },
      cell: ({ row }) => (
        <div className="tabular-nums">
          <div className="text-sm">Biller <span className="font-mono">{row.original.billerCode}</span></div>
          <div className="text-caption text-muted-foreground">Ref <span className="font-mono">{row.original.customerReference}</span></div>
        </div>
      ),
    },
    {
      id: 'amount',
      accessorFn: (b) => b.amount ?? -1,
      header: 'Amount',
      meta: { className: 'text-right', headerClassName: 'text-right' },
      cell: ({ row }) => row.original.amount == null
        ? <span className="text-caption text-muted-foreground" title="Set ASIC’s fee in Settings">Fee not set</span>
        : <span className="font-mono text-sm tabular-nums">{fmtMoney2(row.original.amount)}</span>,
    },
    {
      id: 'status',
      header: 'Payment',
      enableSorting: false,
      cell: ({ row }) => {
        const b = row.original
        return (
          <div className="max-w-64">
            {b.paidAt
              ? <Status entry={{ label: `Paid ${relativeTime(b.paidAt)}`, meaning: 'done' }} />
              : <Status kind="bpay" status={b.transferStatus ?? 'TO_PAY'} />}
            {b.error ? <div className="mt-1 break-words whitespace-normal text-caption text-muted-foreground">{b.error}</div> : null}
            {b.transferId ? <div className="mt-1 font-mono text-caption break-all whitespace-normal text-muted-foreground">{b.transferId}</div> : null}
          </div>
        )
      },
    },
    {
      id: 'lodged',
      accessorFn: (b) => b.lodgedAt,
      header: 'Lodged',
      meta: { hideBelow: 'md' },
      cell: ({ row }) => (
        <div className="tabular-nums">
          <div className="text-sm">{relativeTime(row.original.lodgedAt)}</div>
          {row.original.transactionReference ? <div className="font-mono text-caption break-all whitespace-normal text-muted-foreground">{row.original.transactionReference}</div> : null}
        </div>
      ),
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [selected, allChosen, payable.length])

  const owed = toPay.reduce((s, b) => s + (b.amount ?? 0), 0)

  return (
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <PageHeader
        title="BPAY to pay"
        subtitle="Renewals lodged with ASIC to pay by BPAY, one Airwallex payment per bill."
        right={
          <>
            <Button variant="outline" size="sm" onClick={sync} disabled={syncMutation.isPending} title="Read the latest status of payments in flight (also runs every 10 minutes)">
              <RotateCw className={cn(syncMutation.isPending && 'animate-spin')} />
              Check Airwallex
            </Button>
            <RefreshButton onClick={() => void refetch()} busy={isFetching} />
          </>
        }
      />

      <div className={cn('space-y-3', ((data && !data.airwallexReady) || data?.sandbox || missingFee) && 'mb-6')}>
        {data && !data.airwallexReady ? (
          <Alert>
            <TriangleAlert />
            <AlertDescription>
              <span>Airwallex isn’t set up. Add it in <Link to="/admin/settings" className="text-link hover:underline">Settings</Link>.</span>
            </AlertDescription>
          </Alert>
        ) : null}
        {data?.airwallexReady && data.sandbox ? (
          <Alert>
            <Info />
            <AlertDescription>Airwallex is in sandbox mode: payments are simulated and ASIC is not paid.</AlertDescription>
          </Alert>
        ) : null}
        {missingFee ? (
          <Alert>
            <TriangleAlert />
            <AlertDescription>
              <span>Some bills have no amount: set ASIC’s fee in <Link to="/admin/settings" className="text-link hover:underline">Settings</Link>.</span>
            </AlertDescription>
          </Alert>
        ) : null}
      </div>

      <Skeletonizer loading={isPending}>
      <StatStrip>
        <Stat
          label="To pay"
          value={toPay.length.toLocaleString()}
          sub={toPay.length > 0 ? `${fmtMoney2(owed)} at ASIC’s fees` : 'Nothing owed'}
          dot={toPay.length > 0 ? 'action' : null}
        />
        <Stat label="Waiting on Airwallex" value={inFlight.length.toLocaleString()} sub="Sent, not paid yet" />
        <Stat label="Ticked to pay now" value={fmtMoney2(total)} sub={`${chosen.length} ${chosen.length === 1 ? 'bill' : 'bills'}`} />
      </StatStrip>
      </Skeletonizer>

      <div className="mt-6 flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Checkbox id="bpay-show-paid" checked={showPaid} onCheckedChange={(v) => setShowPaid(v === true)} />
          <Label htmlFor="bpay-show-paid" className="font-normal text-muted-foreground">Show bills paid in the last 30 days</Label>
        </div>
        <Button size="sm" className="w-full sm:w-auto" onClick={() => setConfirming(true)} disabled={chosen.length === 0 || !data?.airwallexReady || payMutation.isPending}>
          <Send /> Pay {chosen.length || ''} {chosen.length === 1 ? 'bill' : 'bills'}{chosen.length ? ` · ${fmtMoney2(total)}` : ''}
        </Button>
      </div>

      <div className="mt-4">
        <DataTable
          columns={columns}
          data={items}
          loading={isPending || (isFetching && isPlaceholderData)}
          skeleton={SKELETON_ROWS}
          empty={<EmptyState title="No BPAY bills to pay" message="Renewals lodged to pay later by BPAY show up here." />}
        />
      </div>

      <Dialog open={confirming} onOpenChange={(o) => { if (!o && !payMutation.isPending) setConfirming(false) }}>
        <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>Pay {chosen.length} BPAY {chosen.length === 1 ? 'bill' : 'bills'}, {fmtMoney2(total)}</DialogTitle>
            <DialogDescription>
              {data?.sandbox ? 'Sandbox: no money moves.' : 'Paid to ASIC through Airwallex, one payment per bill.'}
            </DialogDescription>
          </DialogHeader>
          <DialogBody className="space-y-4">
            <ul className="divide-y rounded-lg border text-sm">
              {chosen.map((b) => (
                <li key={b.id} className="flex items-center justify-between gap-3 px-3 py-2">
                  <span className="min-w-0 truncate">
                    {b.businessName} <span className="text-caption text-muted-foreground">ref <span className="font-mono">{b.customerReference}</span></span>
                  </span>
                  <span className="font-mono tabular-nums">{fmtMoney2(b.amount ?? 0)}</span>
                </li>
              ))}
            </ul>
            {data?.sandbox ? null : (
              <Alert>
                <TriangleAlert />
                <AlertDescription>Paid from your AUD balance. This can’t be undone from here.</AlertDescription>
              </Alert>
            )}
          </DialogBody>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirming(false)} disabled={payMutation.isPending}>Cancel</Button>
            <Button onClick={pay} disabled={payMutation.isPending}>
              {payMutation.isPending ? 'Paying…' : `Pay ${fmtMoney2(total)}`}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}

