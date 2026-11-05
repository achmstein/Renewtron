import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Loader2, TriangleAlert, Users, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type ClientLinkResult, type ClientListItem } from '../api/client'
import { Pagination, useDebouncedValue } from './_components'
import { EmptyState, PageHeader, RefreshButton, Stat, StatStrip } from './_ui'
import { fmtDate, formatAbn, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'

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
      meta: { sticky: true, className: 'min-w-32 max-w-[40vw] sm:min-w-56 sm:max-w-80' },
      cell: ({ row }) => {
        const c = row.original
        return (
          <div className="min-w-0">
            <Link to={`/admin/clients/${c.id}`} className="block truncate text-sm font-medium text-link hover:underline">{c.fullName || '—'}</Link>
            <div className="hidden truncate text-caption text-muted-foreground sm:block">
              {c.email ?? '—'}{c.emailCount > 1 ? ` +${c.emailCount - 1}` : ''}
            </div>
            {c.possibleDuplicateOfId ? (
              <div className="mt-0.5 truncate text-caption text-muted-foreground">
                May be <Link to={`/admin/clients/${c.possibleDuplicateOfId}`} className="text-link hover:underline">{c.possibleDuplicateOfName ?? 'another client'}</Link>
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
        if (c.activeNames === 0) return <span className="text-sm text-muted-foreground">None</span>
        return (
          <div className="max-w-64">
            <div className="truncate text-sm">{c.firstName}</div>
            {c.activeNames > 1 ? <div className="text-caption text-muted-foreground tabular-nums">+{c.activeNames - 1} more</div> : null}
          </div>
        )
      },
    },
    {
      id: 'abn',
      header: 'ABN',
      enableSorting: false,
      cell: ({ row }) => (
        <div className="font-mono text-sm tabular-nums">
          {row.original.abns.length ? formatAbn(row.original.abns[0]) : <span className="text-muted-foreground">—</span>}
          {row.original.abns.length > 1 ? <span className="text-muted-foreground"> +{row.original.abns.length - 1}</span> : null}
        </div>
      ),
    },
    {
      id: 'due',
      header: 'Next due',
      enableSorting: false,
      cell: ({ row }) => row.original.nextDue
        ? <span className="text-sm tabular-nums">{fmtDate(row.original.nextDue)}</span>
        : <span className="text-sm text-muted-foreground">Unknown</span>,
    },
    {
      id: 'flags',
      header: 'Status',
      enableSorting: false,
      cell: ({ row }) => (
        <div className="flex flex-wrap gap-1">
          {row.original.hasPurchased ? <Badge variant="secondary">Customer</Badge> : <Badge variant="outline">Lead</Badge>}
          {row.original.keys > 0 ? <Badge variant="outline">{row.original.keys} key{row.original.keys === 1 ? '' : 's'}</Badge> : null}
          {row.original.isTest ? <Badge variant="outline">Test</Badge> : null}
        </div>
      ),
    },
    {
      accessorKey: 'lastSeenAt',
      header: 'Last seen',
      cell: ({ row }) => <span className="text-sm tabular-nums">{relativeTime(row.original.lastSeenAt)}</span>,
    },
  ], [])

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        title="Clients"
        subtitle="One record per person, with every business name they hold."
        right={<RefreshButton onClick={() => { void refetch(); void refetchStats() }} busy={isFetching} />}
      />

      <div className="space-y-4">
        {stats && stats.clients > 0 ? (
          <StatStrip>
            <Stat label="Clients" value={stats.clients.toLocaleString()} sub={`${stats.purchased.toLocaleString()} bought from us`} />
            <Stat label="Business names" value={stats.names.toLocaleString()} sub="Active, across all clients" />
            <Stat label="Several names" value={stats.severalNames.toLocaleString()} sub="Clients with more than one" />
            <Stat
              label="Possible duplicates"
              value={stats.possibleDuplicates.toLocaleString()}
              sub="Same name or mobile on an ABN"
              dot={stats.possibleDuplicates > 0 ? 'waiting' : null}
            />
          </StatStrip>
        ) : null}

        {stats && stats.possibleDuplicates > 0 ? (
          <Alert className="flex items-center gap-3 py-3 [&>svg]:translate-y-0">
            <Users />
            <div className="min-w-0 flex-1">
              <AlertTitle>{stats.possibleDuplicates} possible duplicate{stats.possibleDuplicates === 1 ? '' : 's'} to decide</AlertTitle>
              <AlertDescription>Merge or keep apart, one pair at a time.</AlertDescription>
            </div>
            <Button size="sm" variant="outline" asChild><Link to="/admin/clients/duplicates">Review</Link></Button>
          </Alert>
        ) : null}

        <LinkPanel />
      </div>

      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search name, email, ABN or business name…" className="h-8 w-72" />
        <ToggleGroup
          type="single"
          variant="outline"
          size="sm"
          value={filter || 'all'}
          onValueChange={(v) => { if (v) update({ filter: v === 'all' ? '' : v }) }}
        >
          {filters.map((f) => (
            <ToggleGroupItem key={f.value} value={f.value || 'all'} className="px-3">{f.label}</ToggleGroupItem>
          ))}
        </ToggleGroup>
        {search || filter ? (
          <Button variant="ghost" size="sm" onClick={() => { setSearch(''); update({ filter: '', q: '' }) }}>
            Reset <X />
          </Button>
        ) : null}
        {loading ? <Loader2 className="size-4 animate-spin text-muted-foreground" aria-label="Loading" /> : null}
        {data && !loading ? (
          <span className="text-caption text-muted-foreground tabular-nums">{data.totalCount.toLocaleString()} client{data.totalCount === 1 ? '' : 's'}</span>
        ) : null}
      </div>

      <div className="mt-4">
        <DataTable
          columns={columns}
          data={items}
          loading={loading || (!data && isFetching)}
          empty={!data ? <div className="py-10 text-center"><Loader2 className="mx-auto size-6 animate-spin text-muted-foreground" /></div>
            : <EmptyState title="No clients match" message={stats?.clients ? undefined : 'Build the client records above first.'} />}
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
      <div className="flex items-center gap-2 text-caption text-muted-foreground">
        Kept up to date every 15 minutes{last ? ` · last run ${relativeTime(last.finishedAt)}` : ''}
        <Button variant="ghost" size="xs" onClick={() => run.mutate(false)}>Run now</Button>
      </div>
    )
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Build client records</CardTitle>
        <CardDescription title="Groups every Ontraport sale, wizard lead, renewal and key request into one client per person, and adds all the business names under their ABNs from the public register. After the real run, new records are added every 15 minutes.">
          One client per person, from every sale, lead and renewal. Do a dry run first: it changes nothing.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {running ? (
          <div className="flex items-center gap-2 text-sm">
            <Loader2 className="size-4 animate-spin text-muted-foreground" />
            {status.runningDry ? 'Dry run' : 'Building client records'} in progress
            {status.runningSince ? ` · started ${relativeTime(status.runningSince)}` : ''}…
          </div>
        ) : last ? (
          <LinkResult result={last} />
        ) : null}

        <div className="flex flex-wrap gap-2">
          <Button variant="outline" size="sm" disabled={running} onClick={() => run.mutate(true)}>
            {dryDone ? 'Dry run again' : 'Dry run'}
          </Button>
          <Button size="sm" disabled={running || (!dryDone && !status.hasClients)} onClick={() => run.mutate(false)}
            title={!dryDone && !status.hasClients ? 'Do a dry run first' : undefined}>
            Build client records
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

function LinkResult({ result }: { result: ClientLinkResult }) {
  if (result.error) {
    return (
      <Alert variant="destructive">
        <TriangleAlert />
        <AlertDescription>The {result.dryRun ? 'dry run' : 'run'} failed: {result.error}</AlertDescription>
      </Alert>
    )
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
    <div>
      <div className="text-sm font-medium">
        {result.dryRun ? 'Dry run' : 'Run'} finished {relativeTime(result.finishedAt)}
        {result.dryRun ? <span className="font-normal text-muted-foreground"> · nothing was saved. A real run would do this:</span> : null}
      </div>
      <dl className="mt-2 grid grid-cols-2 gap-x-6 sm:grid-cols-4">
        {rows.map(([label, value]) => (
          <div
            key={label}
            className="flex items-baseline justify-between gap-2 border-b py-1.5"
            title={label === 'Left unlinked' && result.skipped > 0
              ? 'Records with no email to reach the person by (some manual renewals), or bulk uploads whose name isn’t on any client yet.'
              : undefined}
          >
            <dt className="text-caption text-muted-foreground">{label}</dt>
            <dd className="text-sm tabular-nums">{value.toLocaleString()}</dd>
          </div>
        ))}
      </dl>
    </div>
  )
}
