import { Children, useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { CalendarRange, ChevronLeft, ChevronRight, Contact, TriangleAlert } from 'lucide-react'
import { api } from '../api/client'
import { TrendChart } from '@/components/trend-chart'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { Dialog, DialogBody, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'

export function ErrorModal({ open, message, onClose, title = 'Error details' }: { open: boolean; message: string; onClose: () => void; title?: string }) {
  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <TriangleAlert className="size-4 text-destructive" />
            {title}
          </DialogTitle>
        </DialogHeader>
        <DialogBody className="max-h-[50dvh] overflow-y-auto">
          <p className="rounded-md bg-muted/50 p-3 font-mono text-caption break-words whitespace-pre-wrap">{message}</p>
        </DialogBody>
        <DialogFooter>
          <Button onClick={onClose}>Close</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function Pagination({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pageCount = Math.max(1, Math.ceil(total / pageSize))
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, total)
  const last = page * pageSize >= total
  return (
    <div className="mt-4 flex items-center justify-between gap-3">
      <p className="text-sm text-muted-foreground tabular-nums">
        {from.toLocaleString()}–{to.toLocaleString()} of {total.toLocaleString()}
      </p>
      <div className="flex items-center gap-2">
        <span className="hidden text-sm text-muted-foreground tabular-nums sm:inline">Page {page} of {pageCount}</span>
        <Button variant="outline" size="icon-sm" onClick={() => onPage(page - 1)} disabled={page === 1} title="Previous page" aria-label="Previous page">
          <ChevronLeft />
        </Button>
        <Button variant="outline" size="icon-sm" onClick={() => onPage(page + 1)} disabled={last} title="Next page" aria-label="Next page">
          <ChevronRight />
        </Button>
      </div>
    </div>
  )
}

/**
 * "Client: Liza Lange →" for a lead, renewal, sale, key request or key letter. Renders nothing
 * until the record is on a client (the linker attaches new records every 15 minutes).
 */
export function ClientLink({ kind, id, className = '' }: {
  kind: 'lead' | 'renewal' | 'sale' | 'keyRequest' | 'keyLetter'
  id: string
  className?: string
}) {
  const { data } = useQuery({
    queryKey: ['admin-client-lookup', kind, id],
    queryFn: () => api.admin.clientLookup(kind, id),
    retry: false,
    staleTime: 60_000,
  })
  if (!data) return null
  return (
    <Link
      to={`/admin/clients/${data.id}`}
      title="Open the client"
      className={cn('inline-flex items-center gap-1.5 text-sm font-medium text-link hover:underline', className)}
    >
      <Contact className="size-4" />
      {data.fullName || 'Unnamed client'}
      {data.possibleDuplicate ? <span className="font-normal text-muted-foreground">· possible duplicate</span> : null}
    </Link>
  )
}

export function useDebouncedValue<T>(value: T, delay = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const t = window.setTimeout(() => setV(value), delay)
    return () => window.clearTimeout(t)
  }, [value, delay])
  return v
}

/* ─────────────────── Stats (see STYLE.md "Stats") ─────────────────── */

const STAT_DOT = { action: 'bg-destructive', waiting: 'bg-warning' } as const

/** Related stats in one card, split by thin lines (stacked on phones). Two to four read best. */
export function StatStrip({ children }: { children: ReactNode }) {
  const n = Children.toArray(children).filter(Boolean).length
  return (
    <Card className={cn(
      'grid gap-0 divide-y py-0 sm:divide-x sm:divide-y-0',
      n >= 4 ? 'sm:grid-cols-4' : n === 3 ? 'sm:grid-cols-3' : 'sm:grid-cols-2',
    )}>
      {children}
    </Card>
  )
}

/**
 * One quiet stat: a label, a black number, one muted line. `dot` marks a status beside the label;
 * `trend` draws a small sparkline under the number (the stat's only colour).
 */
export function Stat({ label, value, sub, period, dot, hint, trend }: {
  label: string
  value: ReactNode
  sub?: ReactNode
  /** "Last 30 days", "This month": sentence case, on the right of the label. */
  period?: string
  dot?: keyof typeof STAT_DOT | null
  /** Explanation on hover. */
  hint?: string
  trend?: Array<{ date?: string; count: number }>
}) {
  return (
    <div className="flex min-w-0 flex-col px-5 py-4" title={hint}>
      <div className="flex items-center justify-between gap-2 text-caption text-muted-foreground">
        <span className="flex min-w-0 items-center gap-2 truncate">
          {dot ? <span aria-hidden className={cn('size-2 shrink-0 rounded-full', STAT_DOT[dot])} /> : null}
          {label}
        </span>
        {period ? <span className="shrink-0">{period}</span> : null}
      </div>
      <div className="mt-1 text-2xl font-semibold tracking-tight tabular-nums">{value}</div>
      {sub ? <div className="truncate text-caption text-muted-foreground tabular-nums">{sub}</div> : null}
      {trend && trend.length > 0 ? <div className="mt-2 -mb-1 h-8"><TrendChart data={trend} /></div> : null}
    </div>
  )
}

/* ─────────────────── Date range filter (list toolbars) ─────────────────── */

/** "Dates" in a list's filter row: a from/to pair in a popover, shown as a badge once set. */
export function DateRangeFilter({ dateFrom, dateTo, onFrom, onTo }: {
  dateFrom: string
  dateTo: string
  onFrom: (v: string) => void
  onTo: (v: string) => void
}) {
  const active = dateFrom || dateTo
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" className="border-dashed">
          <CalendarRange />
          Dates
          {active ? <Badge variant="secondary" className="rounded-sm px-1 font-normal tabular-nums">{dateFrom || '…'} → {dateTo || '…'}</Badge> : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-64 space-y-3" align="start">
        <div className="grid gap-1.5">
          <Label htmlFor="date-from">From</Label>
          <Input id="date-from" type="date" value={dateFrom} onChange={(e) => onFrom(e.target.value)} className="tabular-nums" />
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="date-to">To</Label>
          <Input id="date-to" type="date" value={dateTo} onChange={(e) => onTo(e.target.value)} className="tabular-nums" />
        </div>
        {active ? (
          <Button variant="ghost" size="sm" className="w-full" onClick={() => { onFrom(''); onTo('') }}>Clear dates</Button>
        ) : null}
      </PopoverContent>
    </Popover>
  )
}
