import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Loader2, TriangleAlert } from 'lucide-react'
import { useState } from 'react'
import { api, type TodayGroup, type TodayItem } from '../api/client'
import { Queue, useDoneItems, useRowKeyboard } from './_rows'
import { RefreshButton } from './_ui'
import { fmtMoney0 } from './_utils'
import { LinkButton, WorkRow, workKey } from './_work'
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
    queryFn: () => api.admin.today(),
    refetchInterval: 120_000,
  })

  const now = new Date()
  const paid = data?.groups.find((g) => g.key === 'paid')
  const { done, mark, undo } = useDoneItems()
  useRowKeyboard(mark)
  const marked = data?.groups.flatMap((g) => g.items).filter((i) => done.has(workKey(i))) ?? []
  const open = data ? data.total - marked.length : 0

  return (
    <div className="mx-auto max-w-5xl px-4 py-8 sm:px-6 lg:px-8">
      <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
        <div>
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">
            {now.toLocaleDateString('en-AU', { weekday: 'long', day: 'numeric', month: 'long' })}
          </div>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight text-zinc-900">
            {!data ? 'Today' : open <= 0 ? 'Nothing needs a person right now' : `${open.toLocaleString()} thing${open === 1 ? '' : 's'} need${open === 1 ? 's' : ''} a person`}
          </h1>
          {paid && paid.total ? (
            <p className="mt-1 text-sm text-zinc-500">{fmtMoney0(paid.total)} that clients paid is waiting on us.</p>
          ) : null}
          <p className="mt-1 hidden text-xs text-zinc-400 lg:block">
            <Kbd>↑</Kbd> <Kbd>↓</Kbd> move · <Kbd>Enter</Kbd> open · <Kbd>D</Kbd> done · <Kbd>Ctrl K</Kbd> find a client
          </p>
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
          {data.groups.map((g) => <Group key={g.key} group={g} done={done} />)}
          {marked.length ? <MarkedDone items={marked} undo={undo} /> : null}
        </div>
      )}
    </div>
  )
}

function Group({ group, done }: { group: TodayGroup; done: Set<string> }) {
  const items = group.items.filter((i) => !done.has(workKey(i)))
  const hidden = group.count - group.items.length
  return (
    <Queue
      title={GROUP_TITLES[group.key]}
      count={group.count - (group.items.length - items.length)}
      total={group.total ? fmtMoney0(group.total) : undefined}
      action={<GroupActions group={group} />}
      empty={EMPTY[group.key]}
    >
      {items.map((item) => <WorkRow key={`${item.kind}-${item.id}`} item={item} />)}
      {hidden > 0 ? (
        <div className="border-t border-zinc-100 px-4 py-2 text-xs text-zinc-500">
          {group.items.length} of {group.count.toLocaleString()} shown, most urgent first. The rest are in the lists linked above.
        </div>
      ) : null}
    </Queue>
  )
}

/** Items marked done on this computer, kept within reach in case of a slip. */
function MarkedDone({ items, undo }: { items: TodayItem[]; undo: (key: string) => void }) {
  const [show, setShow] = useState(false)
  return (
    <section className="rounded-xl bg-white ring-1 ring-zinc-200">
      <button type="button" onClick={() => setShow((v) => !v)} className="w-full px-4 py-2.5 text-left text-sm text-zinc-600">
        {items.length} marked done on this computer. They come back if anything about them changes. <span className="text-brand-700">{show ? 'Hide' : 'Show'}</span>
      </button>
      {show ? items.map((i) => (
        <div key={workKey(i)} className="flex items-center gap-2 border-t border-zinc-100 pr-3 opacity-60">
          <div className="min-w-0 flex-1"><WorkRow item={i} /></div>
          <Button size="xs" variant="outline" onClick={() => undo(workKey(i))}>Undo</Button>
        </div>
      )) : null}
    </section>
  )
}

function Kbd({ children }: { children: string }) {
  return <kbd className="rounded border border-zinc-200 bg-white px-1 font-mono text-xxs text-zinc-500">{children}</kbd>
}

const EMPTY: Record<TodayGroup['key'], string> = {
  paid: 'Every paid renewal is done or on its way.',
  bpay: 'No ASIC bills waiting.',
  keys: 'No key requests to send and no key letters to sort.',
  decide: 'Nothing to decide.',
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

