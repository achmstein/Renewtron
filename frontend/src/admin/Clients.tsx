import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Loader2, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type ClientLinkResult, type ClientListItem } from '../api/client'
import { Pagination, useDebouncedValue } from './_components'
import { EmptyState, PageHeader, RefreshButton, StatTile, StatusPill } from './_ui'
import { fmtDate, formatAbn, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

const filters = [
  { value: '', label: 'All' },
  { value: 'several', label: 'Several names' },
  { value: 'purchased', label: 'Bought from us' },
  { value: 'duplicates', label: 'Possible duplicates' },
] as const

const PAGE_SIZE = 25

export default function Clients() {
  // Filter, search and page live in the URL, so Back and refresh keep your place and a view can be shared.
  const [params, setParams] = useSearchParams()
  const filter = params.get('filter') ?? ''
  const page = Math.max(1, Number(params.get('page')) || 1)
  const urlSearch = params.get('q') ?? ''
  const [search, setSearch] = useState(urlSearch)
  const debouncedSearch = useDebouncedValue(search.trim())

  const update = (next: { filter?: string; page?: number; q?: string }) => {
    setParams((p) => {
      const n = new URLSearchParams(p)
      const set = (k: string, v: string | undefined) => (v ? n.set(k, v) : n.delete(k))
      if (next.filter !== undefined) set('filter', next.filter)
      if (next.q !== undefined) set('q', next.q)
      // A new filter or search starts at page one.
      set('page', next.page && next.page > 1 ? String(next.page) : undefined)
      return n
    }, { replace: next.page === undefined })
  }
  useEffect(() => {
    if (debouncedSearch !== urlSearch) update({ q: debouncedSearch })
    // Only when the typed search settles; update is a fresh closure each render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debouncedSearch])

  const { data, isFetching, isPlaceholderData, refetch } = useQuery({
    queryKey: ['admin-clients', { search: urlSearch, filter, page }],
    queryFn: () => api.admin.clients({ search: urlSearch || undefined, filter: filter || undefined, page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  })
  const { data: stats, refetch: refetchStats } = useQuery({
    queryKey: ['admin-clients', 'stats'],
    queryFn: () => api.admin.clientStats(),
    staleTime: 60_000,
  })
  const items = data?.items ?? []
  // Typing ahead of the debounce counts as loading too, so the table never looks finished when it isn't.
  const loading = (isFetching && isPlaceholderData) || search.trim() !== urlSearch

  const columns = useMemo<DataTableColumn<ClientListItem>[]>(() => [
    {
      id: 'client',
      accessorKey: 'fullName',
      header: 'Client',
      meta: { sticky: true, className: 'min-w-[8rem] max-w-[40vw] sm:min-w-[14rem] sm:max-w-[20rem]' },
      cell: ({ row }) => {
        const c = row.original
        return (
          <div className="min-w-0">
            <Link to={`/admin/clients/${c.id}`} className="block text-sm font-medium text-foreground hover:underline truncate">{c.fullName || '—'}</Link>
            <div className="hidden sm:block text-xxs font-mono text-muted-foreground/70 truncate">
              {c.email ?? '—'}{c.emailCount > 1 ? ` +${c.emailCount - 1}` : ''}
            </div>
            {c.possibleDuplicateOfId ? (
              <div className="mt-0.5 text-xxs text-warning truncate">
                May be the same person as <Link to={`/admin/clients/${c.possibleDuplicateOfId}`} className="underline">{c.possibleDuplicateOfName ?? 'another client'}</Link>
              </div>
            ) : null}
          </div>
        )
      },
    },
    {
      id: 'names',
      header: 'Business names',
      enableSorting: false,
      cell: ({ row }) => {
        const c = row.original
        if (c.activeNames === 0) return <span className="text-xxs font-mono text-muted-foreground/70">none</span>
        return (
          <div>
            <div className="text-sm text-foreground truncate max-w-[16rem]">{c.firstName}</div>
            {c.activeNames > 1 ? <div className="text-xxs font-mono text-muted-foreground tabular-nums">+{c.activeNames - 1} more</div> : null}
          </div>
        )
      },
    },
    {
      id: 'abn',
      header: 'ABN',
      enableSorting: false,
      cell: ({ row }) => (
        <div className="text-xs font-mono tabular-nums text-muted-foreground">
          {row.original.abns.length ? formatAbn(row.original.abns[0]) : '—'}
          {row.original.abns.length > 1 ? <span className="text-muted-foreground/70"> +{row.original.abns.length - 1}</span> : null}
        </div>
      ),
    },
    {
      id: 'due',
      header: 'Next due',
      enableSorting: false,
      cell: ({ row }) => row.original.nextDue
        ? <span className="text-sm font-mono tabular-nums text-foreground">{fmtDate(row.original.nextDue)}</span>
        : <span className="text-xxs font-mono text-muted-foreground/70">unknown</span>,
    },
    {
      id: 'flags',
      header: 'Status',
      enableSorting: false,
      cell: ({ row }) => (
        <div className="flex flex-wrap gap-1">
          {row.original.hasPurchased ? <StatusPill tone="emerald">CUSTOMER</StatusPill> : <StatusPill tone="zinc">LEAD</StatusPill>}
          {row.original.keys > 0 ? <StatusPill tone="indigo">{row.original.keys} KEY{row.original.keys === 1 ? '' : 'S'}</StatusPill> : null}
          {row.original.isTest ? <StatusPill tone="amber">TEST</StatusPill> : null}
        </div>
      ),
    },
    {
      accessorKey: 'lastSeenAt',
      header: 'Last seen',
      cell: ({ row }) => <span className="text-sm text-foreground tabular-nums">{relativeTime(row.original.lastSeenAt)}</span>,
    },
  ], [])

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="CUSTOMERS"
        title="Clients"
        subtitle="One record per person, with every business name they hold."
        right={<RefreshButton onClick={() => { void refetch(); void refetchStats() }} busy={isFetching} />}
      />

      <LinkPanel />

      {stats && stats.clients > 0 ? (
        <div className="mt-6 grid grid-cols-2 sm:grid-cols-4 gap-4">
          <StatTile label="Clients" value={stats.clients.toLocaleString()} sub={`${stats.purchased.toLocaleString()} bought from us`} />
          <StatTile label="Business names" value={stats.names.toLocaleString()} sub="active, across all clients" />
          <StatTile label="Several names" value={stats.severalNames.toLocaleString()} sub="clients with more than one" tone={stats.severalNames > 0 ? 'emerald' : 'zinc'} />
          <StatTile label="Possible duplicates" value={stats.possibleDuplicates.toLocaleString()} sub="same name or mobile on an ABN" tone={stats.possibleDuplicates > 0 ? 'amber' : 'zinc'} />
        </div>
      ) : null}

      {stats && stats.possibleDuplicates > 0 ? (
        <div className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-xl bg-warning/5 px-5 py-3 ring-1 ring-warning/25">
          <div className="text-sm text-warning">
            <span className="font-medium">{stats.possibleDuplicates} possible duplicate{stats.possibleDuplicates === 1 ? '' : 's'}</span> to decide:
            merge or keep apart, one pair at a time.
          </div>
          <Button size="sm" asChild><Link to="/admin/clients/duplicates">Review duplicates</Link></Button>
        </div>
      ) : null}

      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search name, email, ABN or business name…" className="h-9 w-72" />
        <div className="inline-flex rounded-md ring-1 ring-border overflow-hidden" role="group">
          {filters.map((f) => (
            <button
              key={f.value}
              type="button"
              onClick={() => update({ filter: f.value })}
              className={`px-3 h-9 text-sm ${filter === f.value ? 'bg-zinc-900 text-white' : 'bg-card text-foreground hover:bg-muted/50'}`}
            >
              {f.label}
            </button>
          ))}
        </div>
        {search || filter ? (
          <Button variant="ghost" size="sm" className="h-9" onClick={() => { setSearch(''); update({ filter: '', q: '' }) }}>
            Reset <X className="h-4 w-4" />
          </Button>
        ) : null}
        {loading ? <Loader2 className="h-4 w-4 animate-spin text-link" aria-label="Loading" /> : null}
        {data && !loading ? (
          <span className="text-xs font-mono tabular-nums text-muted-foreground">{data.totalCount.toLocaleString()} client{data.totalCount === 1 ? '' : 's'}</span>
        ) : null}
      </div>

      <div className="mt-4">
        <DataTable
          columns={columns}
          data={items}
          loading={loading || (!data && isFetching)}
          empty={!data ? <div className="py-10 text-center"><Loader2 className="mx-auto h-6 w-6 animate-spin text-link" /></div>
            : <EmptyState title="No clients match." message={stats?.clients ? undefined : 'Build the client records above first.'} />}
        />
        {data ? <Pagination page={page} pageSize={PAGE_SIZE} total={data.totalCount} onPage={(p) => update({ page: p })} /> : null}
      </div>
    </div>
  )
}

/** Building the records: a dry run reports what would happen, then a real run writes it. */
function LinkPanel() {
  const queryClient = useQueryClient()
  const { data: status } = useQuery({
    queryKey: ['admin-client-link-status'],
    queryFn: () => api.admin.clientLinkStatus(),
    refetchInterval: (q) => (q.state.data?.running ? 3000 : false),
  })
  const run = useMutation({
    mutationFn: (dryRun: boolean) => api.admin.runClientLink(dryRun),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['admin-client-link-status'] }),
    onError: (e) => sileo.error({ title: 'Could not start the run', description: e instanceof Error ? e.message : undefined }),
  })

  const last = status?.last
  // Refresh the list when a real run finishes.
  const finishedRun = last && !last.dryRun ? last.finishedAt : null
  useEffect(() => {
    if (finishedRun) void queryClient.invalidateQueries({ queryKey: ['admin-clients'] })
  }, [finishedRun, queryClient])

  if (!status) return null
  const running = status.running || run.isPending
  const dryDone = last?.dryRun && !last.error

  // Once records exist the recurring job keeps them up to date; the panel shrinks to a status line.
  if (status.hasClients && !running && !(last && last.dryRun)) {
    return (
      <div className="mt-4 text-xxs font-mono text-muted-foreground">
        Kept up to date every 15 minutes{last ? ` · last run ${relativeTime(last.finishedAt)}` : ''}.{' '}
        <button type="button" className="underline hover:text-foreground" onClick={() => run.mutate(false)}>Run now</button>
      </div>
    )
  }

  return (
    <div className="mt-6 rounded-xl bg-card p-5 ring-1 ring-border shadow-sm">
      <div className="text-xs font-medium text-link">BUILD CLIENT RECORDS</div>
      <p className="mt-1 text-sm text-muted-foreground max-w-3xl">
        Groups every Ontraport sale, wizard lead, renewal and key request into one client per person, and adds all the business
        names under their ABNs from the public register. Do a dry run first: it reports what it would do and changes nothing.
        After the real run, new records are added every 15 minutes.
      </p>

      {running ? (
        <div className="mt-4 flex items-center gap-2 text-sm text-foreground">
          <Loader2 className="h-4 w-4 animate-spin text-link" />
          {status.runningDry ? 'Dry run' : 'Building client records'} in progress
          {status.runningSince ? ` · started ${relativeTime(status.runningSince)}` : ''}…
        </div>
      ) : last ? (
        <LinkResult result={last} />
      ) : null}

      <div className="mt-4 flex flex-wrap gap-2">
        <Button variant="outline" size="sm" disabled={running} onClick={() => run.mutate(true)}>
          {dryDone ? 'Dry run again' : 'Dry run'}
        </Button>
        <Button size="sm" disabled={running || (!dryDone && !status.hasClients)} onClick={() => run.mutate(false)}
          title={!dryDone && !status.hasClients ? 'Do a dry run first' : undefined}>
          Build client records
        </Button>
      </div>
    </div>
  )
}

function LinkResult({ result }: { result: ClientLinkResult }) {
  if (result.error) {
    return <div className="mt-4 rounded-md bg-destructive/5 ring-1 ring-destructive/20 px-3 py-2 text-sm text-destructive">The {result.dryRun ? 'dry run' : 'run'} failed: {result.error}</div>
  }
  const rows: Array<[string, number]> = [
    ['Clients created', result.clientsCreated],
    ['Possible duplicates', result.possibleDuplicates],
    ['Business names added', result.namesAdded],
    ['Names cancelled on the register', result.namesCancelled],
    ['Email addresses', result.emailsAdded],
    ['ABNs (register checked)', result.abnsChecked],
    ['Due dates set', result.dueDatesSet],
    ['ASIC keys put on names', result.keysSet],
    ['Ontraport sales linked', result.salesLinked],
    ['Wizard leads linked', result.leadsLinked],
    ['Renewals linked', result.renewalsLinked],
    ['Renewals completed since linking', result.renewalsCompletedLater],
    ['Key requests linked', result.keyRequestsLinked],
    ['Bulk uploads linked', result.bulkUploadsLinked],
    ['Messages linked', result.messagesLinked],
    ['Key letters linked', result.keyLettersLinked],
    ['"No contact" keys resolved', result.keyLettersResolved],
    ['Left unlinked', result.skipped],
  ]
  return (
    <div className="mt-4">
      <div className="text-sm font-medium text-foreground">
        {result.dryRun ? 'Dry run' : 'Run'} finished {relativeTime(result.finishedAt)}
        {result.dryRun ? <span className="text-muted-foreground font-normal"> — nothing was saved. This is what a real run would do:</span> : null}
      </div>
      <dl className="mt-2 grid grid-cols-2 sm:grid-cols-4 gap-x-6 gap-y-1">
        {rows.map(([label, value]) => (
          <div key={label} className="flex items-baseline justify-between gap-2 border-b border-border py-1">
            <dt className="text-xs text-muted-foreground">{label}</dt>
            <dd className="text-sm font-mono tabular-nums text-foreground">{value.toLocaleString()}</dd>
          </div>
        ))}
      </dl>
      {result.skipped > 0 ? (
        <p className="mt-2 text-xxs text-muted-foreground">Left unlinked: records with no email to reach the person by (some manual renewals), or bulk uploads whose name isn't on any client yet.</p>
      ) : null}
    </div>
  )
}
