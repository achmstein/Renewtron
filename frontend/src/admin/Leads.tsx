import { useMemo, useState } from 'react'
import { createPortal } from 'react-dom'
import { Link, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CalendarRange, Check, Mail, PlusCircle, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../api/client'
import { useDebouncedValue } from './_components'
import { EmptyState, FunnelPills, PageHeader, RefreshButton, SparklineTile, StatTile, StatusPill, type Tone } from './_ui'
import { durationShort, fmtDate, fmtMoney0, fmtTime, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Command, CommandGroup, CommandItem, CommandList, CommandSeparator,
} from '@/components/ui/command'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Separator } from '@/components/ui/separator'
import { cn } from '@/lib/utils'

type LeadsResponse = Awaited<ReturnType<typeof api.admin.leads>>
type Lead = LeadsResponse['items'][number]
type Stats = LeadsResponse['stats']
type Facets = LeadsResponse['facets']

/** Operator-friendly names for LeadOutcome values. */
const outcomeLabels: Record<string, string> = {
  RenewalAvailable: 'Available',
  RenewalCompleted: 'Converted',
  NotDueForRenewal: 'Not due',
  RenewalInProgress: 'In progress',
  NoBusinessNames: 'No names',
  Pending: 'Pending',
}

const reminderLabels: Record<string, string> = {
  OptedIn: 'Opted in',
  NotOptedIn: 'Not opted in',
}

export default function Leads() {
  // Deep link from the dashboard ("?outcome=RenewalAvailable") seeds the facet on first render.
  const [searchParams] = useSearchParams()
  const [outcomes, setOutcomes] = useState<string[]>(() => searchParams.get('outcome')?.split(',').filter(Boolean) ?? [])
  const [reminders, setReminders] = useState<string[]>([])
  const [hasRenewal, setHasRenewal] = useState('')
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [search, setSearch] = useState('')
  const [winBackOpen, setWinBackOpen] = useState(false)
  const debouncedSearch = useDebouncedValue(search)

  const queryClient = useQueryClient()
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['admin-leads', { outcomes, reminders, search: debouncedSearch, hasRenewal, dateFrom, dateTo }],
    queryFn: () => api.admin.leads({
      outcome: outcomes.join(',') || undefined,
      reminder: reminders.join(',') || undefined,
      search: debouncedSearch || undefined,
      hasRenewal: hasRenewal || undefined,
      dateFrom: dateFrom || undefined,
      dateTo: dateTo || undefined,
    }),
    placeholderData: keepPreviousData,
  })
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin-leads'] })

  const leads = data?.items ?? []
  const totalCount = data?.totalCount ?? 0
  const stats: Stats = data?.stats ?? defaultStats()
  const facets: Facets = data?.facets ?? { outcome: [], reminder: [] }

  const outcomeOptions: FacetOption[] = facets.outcome.map((f) => ({ value: f.value, label: outcomeLabels[f.value] ?? f.value, count: f.count }))
  const reminderOptions: FacetOption[] = facets.reminder.map((f) => ({ value: f.value, label: reminderLabels[f.value] ?? f.value, count: f.count }))

  const hasActiveFilters = !!search || outcomes.length > 0 || reminders.length > 0 || !!hasRenewal || !!dateFrom || !!dateTo
  const resetFilters = () => {
    setSearch(''); setOutcomes([]); setReminders([]); setHasRenewal(''); setDateFrom(''); setDateTo('')
  }

  const winBackEnabled = stats.winBackEligible > 0

  // Win-back endpoints take a single outcome / a boolean — pass them only when the
  // facet selection is unambiguous (exactly one value picked).
  const winBackFilter = {
    outcome: outcomes.length === 1 ? outcomes[0] : undefined,
    search: search || undefined,
    reminderOptIn: reminders.length === 1 ? reminders[0] === 'OptedIn' : undefined,
  }
  const winBackMutation = useMutation({
    mutationFn: () => api.admin.winBackSend(winBackFilter),
    onSuccess: () => setWinBackOpen(false),
    onSettled: () => void invalidate(),
  })
  const sendWinBack = () => {
    void sileo.promise(winBackMutation.mutateAsync(), {
      loading: { title: 'Sending win-back emails…' },
      success: (r) => ({ title: `Win-back email sent to ${r.enqueued} lead${r.enqueued === 1 ? '' : 's'}.` }),
      error: (e) => ({ title: 'Win-back send failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const toggleOutcome = (value: string) => {
    setOutcomes((prev) => prev.includes(value) ? prev.filter((v) => v !== value) : [...prev, value])
  }

  const columns = useMemo<DataTableColumn<Lead>[]>(() => [
    {
      id: 'customer',
      accessorKey: 'fullName',
      header: 'Lead',
      // Compact on phones: name only (no avatar); email + ABN return at sm.
      meta: { sticky: true, className: 'min-w-[7rem] max-w-[38vw] sm:min-w-[14rem] sm:max-w-[19rem]' },
      cell: ({ row }) => {
        const l = row.original
        return (
          <div className="flex items-center gap-3">
            <span className="hidden sm:block"><Avatar name={l.fullName} /></span>
            <div className="min-w-0">
              <Link to={`/admin/leads/${l.id}`} className="block text-sm font-medium text-zinc-900 hover:underline truncate">{l.fullName || '—'}</Link>
              <div className="hidden sm:block text-xxs font-mono text-zinc-400 truncate">{l.email}</div>
              <div className="hidden sm:block text-xs font-mono tabular-nums text-zinc-500 mt-0.5">
                <button type="button" onClick={() => toggleOutcome(l.outcome)} className="hover:underline" title="Toggle this lead's outcome in the filter">{l.abn}</button>
              </div>
            </div>
          </div>
        )
      },
    },
    {
      accessorKey: 'outcome',
      header: 'Outcome',
      cell: ({ row }) => <OutcomePill outcome={row.original.outcome} />,
    },
    {
      id: 'names',
      header: 'Names found',
      enableSorting: false,
      cell: ({ row }) => <NamesCell l={row.original} />,
    },
    {
      id: 'funnel',
      header: 'Funnel',
      enableSorting: false,
      cell: ({ row }) => <FunnelPills stages={leadFunnelStages(row.original)} />,
    },
    {
      id: 'renewal',
      header: 'Renewal',
      enableSorting: false,
      meta: { className: 'text-right', headerClassName: 'text-right' },
      cell: ({ row }) => {
        const r = row.original.renewal
        return r ? (
          <div>
            <div className="text-sm font-mono tabular-nums text-zinc-900">{fmtMoney0(r.amount)}</div>
            <RenewalStatusPill status={r.status} />
          </div>
        ) : <span className="text-xxs font-mono text-zinc-400">—</span>
      },
    },
    {
      accessorKey: 'reminderOptIn',
      header: 'Reminder',
      enableSorting: false,
      cell: ({ row }) =>
        row.original.reminderOptIn ? <StatusPill tone="emerald">OPT-IN</StatusPill> : <span className="text-xxs font-mono text-zinc-400">—</span>,
    },
    {
      accessorKey: 'createdAt',
      header: 'Age',
      cell: ({ row }) => (
        <div>
          <div className="text-sm text-zinc-700 tabular-nums">{relativeTime(row.original.createdAt)}</div>
          <div className="text-xxs font-mono text-zinc-400 tabular-nums">{fmtDate(row.original.createdAt)} · {fmtTime(row.original.createdAt)}</div>
        </div>
      ),
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      enableSorting: false,
      meta: { className: 'text-right' },
      cell: ({ row }) => (
        <Link to={`/admin/leads/${row.original.id}`} className="text-sm font-medium text-brand-700 hover:text-brand-800 whitespace-nowrap">View →</Link>
      ),
    },
  ], [])

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="OPS"
        title="Leads"
        subtitle="Customer enquiries entered into the registry — track, convert, and follow up."
        right={
          <>
            <button
              type="button"
              onClick={() => setWinBackOpen(true)}
              disabled={!winBackEnabled}
              className={`inline-flex items-center gap-2 whitespace-nowrap rounded-md px-3 py-2 text-sm font-medium transition ${
                winBackEnabled
                  ? 'bg-brand-600 text-white hover:bg-brand-700 shadow-sm'
                  : 'bg-zinc-100 text-zinc-400 cursor-not-allowed'
              }`}
            >
              <Mail className="h-4 w-4" />
              Send win-back
              {winBackEnabled ? <span className="ml-0.5 inline-flex items-center justify-center rounded bg-white/20 text-white text-xxs font-mono tabular-nums px-1.5 py-0.5 leading-none">{stats.winBackEligible}</span> : null}
            </button>
            <RefreshButton onClick={() => void refetch()} busy={isFetching} />
          </>
        }
      />

      <StatsStrip stats={stats} />

      {/* Filter toolbar — every facet is resolved server-side with live counts */}
      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search ABN, name, or email…"
          className="h-9 w-56"
        />
        <FacetedFilter title="Outcome" options={outcomeOptions} selected={outcomes} onChange={setOutcomes} />
        <FacetedFilter title="Reminder" options={reminderOptions} selected={reminders} onChange={setReminders} />
        <HasRenewalFilter value={hasRenewal} onChange={setHasRenewal} />
        <DateRangeFilter dateFrom={dateFrom} dateTo={dateTo} onFrom={setDateFrom} onTo={setDateTo} />
        {hasActiveFilters ? (
          <Button variant="ghost" size="sm" className="h-9" onClick={resetFilters}>
            Reset
            <X className="h-4 w-4" />
          </Button>
        ) : null}
      </div>

      <div className="mt-4">
        <DataTable columns={columns} data={leads} empty={<EmptyState title="No leads match the current filters." />} />
        {totalCount > leads.length ? (
          <div className="mt-2 text-xs font-mono text-zinc-400 tabular-nums">Showing the {leads.length} most recent of {totalCount.toLocaleString()} matching leads — narrow with filters to see the rest.</div>
        ) : null}
      </div>

      {/* Win-back modal */}
      {winBackOpen ? (
        <WinBackModal
          filter={winBackFilter}
          busy={winBackMutation.isPending}
          onClose={() => setWinBackOpen(false)}
          onConfirm={sendWinBack}
        />
      ) : null}
    </div>
  )
}

function HasRenewalFilter({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const options = [
    { value: 'true', label: 'Has renewal' },
    { value: 'false', label: 'No renewal' },
  ]
  const current = options.find((o) => o.value === value)
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" className="h-9 border-dashed whitespace-nowrap">
          <PlusCircle className="h-4 w-4" />
          Renewal
          {current ? (
            <>
              <Separator orientation="vertical" className="mx-0.5 h-4" />
              <Badge variant="secondary" className="rounded-sm px-1 font-mono font-normal">{current.label}</Badge>
            </>
          ) : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-48 p-0" align="start">
        <Command>
          <CommandList>
            <CommandGroup>
              {options.map((o) => {
                const isSelected = o.value === value
                return (
                  <CommandItem key={o.value} onSelect={() => onChange(isSelected ? '' : o.value)}>
                    <div className={cn(
                      'flex size-4 items-center justify-center rounded-full border',
                      isSelected ? 'border-primary bg-primary text-primary-foreground' : 'border-input [&_svg]:invisible',
                    )}>
                      <Check className="size-3.5" />
                    </div>
                    <span>{o.label}</span>
                  </CommandItem>
                )
              })}
            </CommandGroup>
            {value ? (
              <>
                <CommandSeparator />
                <CommandGroup>
                  <CommandItem onSelect={() => onChange('')} className="justify-center text-center">Clear</CommandItem>
                </CommandGroup>
              </>
            ) : null}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}

function DateRangeFilter({ dateFrom, dateTo, onFrom, onTo }: {
  dateFrom: string
  dateTo: string
  onFrom: (v: string) => void
  onTo: (v: string) => void
}) {
  const active = dateFrom || dateTo
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" className="h-9 border-dashed whitespace-nowrap">
          <CalendarRange className="h-4 w-4" />
          Dates
          {active ? <span className="font-mono text-xs text-muted-foreground">{dateFrom || '…'} → {dateTo || '…'}</span> : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-64 space-y-3" align="start">
        <div>
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500 mb-1">From</div>
          <Input type="date" value={dateFrom} onChange={(e) => onFrom(e.target.value)} className="font-mono tabular-nums" />
        </div>
        <div>
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500 mb-1">To</div>
          <Input type="date" value={dateTo} onChange={(e) => onTo(e.target.value)} className="font-mono tabular-nums" />
        </div>
        {active ? (
          <Button variant="ghost" size="sm" className="w-full" onClick={() => { onFrom(''); onTo('') }}>Clear dates</Button>
        ) : null}
      </PopoverContent>
    </Popover>
  )
}

function defaultStats(): Stats {
  return {
    totalAllTime: 0,
    total30d: 0,
    converted30d: 0,
    conversionRate30d: 0,
    avgHoursToConvert: null,
    today: 0,
    yesterday: 0,
    deltaPct: null,
    daily14d: [],
    outcomeBreakdown: {},
    winBackEligible: 0,
    winBackRecoverableValue: 0,
    avgBasket: 0,
  }
}

function StatsStrip({ stats }: { stats: Stats }) {
  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
      <StatTile
        kicker="ALL TIME"
        label="Total leads"
        value={stats.totalAllTime.toLocaleString()}
        sub={`${stats.total30d.toLocaleString()} in last 30d`}
      />
      <StatTile
        kicker="30D"
        label="Conversion rate"
        value={`${stats.conversionRate30d}%`}
        sub={`${stats.converted30d.toLocaleString()} converted`}
        tone={stats.conversionRate30d >= 30 ? 'emerald' : stats.conversionRate30d >= 10 ? 'amber' : 'zinc'}
      />
      <StatTile
        kicker="WIN-BACK"
        label="Recoverable"
        value={fmtMoney0(stats.winBackRecoverableValue)}
        sub={`${stats.winBackEligible.toLocaleString()} eligible · ${fmtMoney0(stats.avgBasket)} avg basket`}
        tone={stats.winBackEligible > 0 ? 'emerald' : 'zinc'}
      />
      <SparklineTile
        kicker="14D"
        label="Today"
        value={stats.today.toLocaleString()}
        sub={
          stats.avgHoursToConvert != null
            ? `${stats.yesterday.toLocaleString()} yesterday · avg ${durationShort(Number(stats.avgHoursToConvert))} to convert`
            : `${stats.yesterday.toLocaleString()} yesterday`
        }
        deltaPct={stats.deltaPct}
        data={stats.daily14d.map((d) => d.count)}
        labels={stats.daily14d.map((d) => d.date)}
      />
    </div>
  )
}

/* ─────── Cell components ─────── */

function NamesCell({ l }: { l: Lead }) {
  if (l.searchLog == null) return <span className="text-xxs font-mono text-zinc-400">no search</span>
  if (l.searchLog.resultsCount === 0) return <span className="text-xxs font-mono text-zinc-400">none</span>
  return (
    <div>
      <div className="text-sm text-zinc-900 truncate max-w-[16rem]">{l.firstBusinessName ?? '—'}</div>
      {l.searchLog.resultsCount > 1 ? <div className="text-xxs font-mono text-zinc-400 tabular-nums">+{l.searchLog.resultsCount - 1} more</div> : null}
    </div>
  )
}

function leadFunnelStages(l: Lead): Array<{ label: string; tone: Tone; active: boolean }> {
  const renewalStatus = l.renewal?.status
  const paid = renewalStatus === 'Completed'
  const inflight = renewalStatus === 'Pending' || renewalStatus === 'Processing'
  const failed = renewalStatus === 'Failed'

  return [
    { label: 'LEAD', tone: 'emerald', active: true },
    { label: 'SRCH', tone: 'emerald', active: !!l.searchLog },
    failed
      ? { label: 'FAIL', tone: 'red',     active: true }
      : { label: 'PAID', tone: paid ? 'emerald' : inflight ? 'amber' : 'zinc', active: paid || inflight },
  ]
}

function OutcomePill({ outcome }: { outcome: string }) {
  switch (outcome) {
    case 'RenewalAvailable':  return <StatusPill tone="emerald">RENEW. AVAIL.</StatusPill>
    case 'RenewalCompleted':  return <StatusPill tone="emerald">CONVERTED</StatusPill>
    case 'NotDueForRenewal':  return <StatusPill tone="zinc">NOT DUE</StatusPill>
    case 'RenewalInProgress': return <StatusPill tone="indigo">IN PROGRESS</StatusPill>
    case 'NoBusinessNames':   return <StatusPill tone="amber">NO NAMES</StatusPill>
    case 'Pending':           return <StatusPill tone="zinc">PENDING</StatusPill>
    default:                  return <StatusPill tone="zinc">{outcome.toUpperCase()}</StatusPill>
  }
}

function RenewalStatusPill({ status }: { status: string }) {
  switch (status) {
    case 'Completed':  return <StatusPill tone="emerald">PAID</StatusPill>
    case 'Processing': return <StatusPill tone="indigo">PROC.</StatusPill>
    case 'Pending':    return <StatusPill tone="amber">PEND.</StatusPill>
    case 'Failed':     return <StatusPill tone="red">FAILED</StatusPill>
    default:           return <StatusPill tone="zinc">{status.toUpperCase()}</StatusPill>
  }
}

function Avatar({ name }: { name: string }) {
  const initials = (() => {
    if (!name) return '?'
    const parts = name.split(' ').filter(Boolean)
    if (parts.length >= 2) return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
    return name.slice(0, 2).toUpperCase()
  })()
  return (
    <div className="h-9 w-9 shrink-0 rounded-full bg-zinc-100 ring-1 ring-zinc-200 flex items-center justify-center">
      <span className="text-xxs font-mono font-medium text-zinc-600">{initials}</span>
    </div>
  )
}

/* ───────── Win-back modal ───────── */

function WinBackModal({ filter, busy, onClose, onConfirm }: {
  filter: { outcome?: string; search?: string; reminderOptIn?: boolean }
  busy: boolean
  onClose: () => void
  onConfirm: () => void
}) {
  const { data: preview, isPending, error } = useQuery({
    queryKey: ['admin-winback-preview', filter],
    queryFn: () => api.admin.winBackPreview(filter),
  })

  return createPortal(
    <div className="fixed inset-0 z-[100]" role="dialog" aria-modal="true">
      <div className="absolute inset-0 bg-zinc-950/70 backdrop-blur-sm" onClick={onClose} />
      <div className="absolute inset-0 overflow-y-auto">
        <div className="flex min-h-full items-center justify-center p-4">
          <div className="relative w-full max-w-xl rounded-xl bg-white shadow-xl">
            <div className="px-6 py-5 border-b border-zinc-100">
              <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">WIN-BACK</div>
              <h2 className="mt-0.5 text-lg font-semibold text-zinc-900 tracking-tight">Send win-back email</h2>
              <p className="mt-1 text-sm text-zinc-500">A follow-up email to leads who found a renewable name but never paid.</p>
            </div>
            <div className="px-6 py-5">
              {isPending ? (
                <p className="text-sm text-zinc-500">Loading recipient preview…</p>
              ) : error ? (
                <p className="text-sm text-red-700">{error instanceof Error ? error.message : 'Preview failed'}</p>
              ) : preview ? (
                <PreviewBody preview={preview} />
              ) : null}
            </div>
            <div className="px-6 py-4 border-t border-zinc-100 flex items-center justify-end gap-2">
              <button onClick={onClose} className="inline-flex items-center rounded-md bg-white px-3 py-2 text-sm font-medium text-zinc-700 ring-1 ring-inset ring-zinc-300 hover:bg-zinc-50 transition">Cancel</button>
              <button
                onClick={onConfirm}
                disabled={!preview || preview.recipientCount === 0 || busy}
                className="inline-flex items-center gap-2 rounded-md bg-brand-600 text-white px-4 py-2 text-sm font-medium hover:bg-brand-700 disabled:opacity-40 disabled:cursor-not-allowed transition"
              >
                {busy ? 'Sending…' : preview ? `Send to ${preview.recipientCount}` : 'Send'}
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>,
    document.body,
  )
}

function PreviewBody({ preview }: { preview: Awaited<ReturnType<typeof api.admin.winBackPreview>> }) {
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-4">
        <div>
          <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">Recipients</div>
          <div className="mt-1 text-2xl font-semibold tabular-nums text-zinc-900">{preview.recipientCount.toLocaleString()}</div>
          <div className="text-xxs font-mono text-zinc-400 truncate">{preview.sampleNames.length > 0 ? `e.g. ${preview.sampleNames.join(' · ')}` : '—'}</div>
        </div>
        <div>
          <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">Recoverable</div>
          <div className="mt-1 text-2xl font-semibold tabular-nums text-brand-700">{fmtMoney0(preview.recoverableValue)}</div>
          <div className="text-xxs font-mono text-zinc-400">{fmtMoney0(preview.avgBasket)} avg basket × recipients</div>
        </div>
      </div>

      <div>
        <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 mb-1">Subject</div>
        <div className="rounded-md bg-zinc-50 ring-1 ring-zinc-200 px-3 py-2 text-sm text-zinc-900 font-medium">{preview.subject}</div>
      </div>

      <div>
        <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 mb-1">Body preview</div>
        <pre className="rounded-md bg-zinc-50 ring-1 ring-zinc-200 px-3 py-2 text-xs text-zinc-700 font-mono whitespace-pre-wrap max-h-48 overflow-y-auto">{preview.bodyPreview}</pre>
        <p className="mt-1 text-xxs font-mono text-zinc-400">Edit the template in Settings → Win-back. Merge tags: {'{{FullName}}'}, {'{{Abn}}'}, {'{{BusinessName}}'}, {'{{Email}}'}.</p>
      </div>
    </div>
  )
}
