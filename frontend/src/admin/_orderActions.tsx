import { createContext, Fragment, useContext, useState, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Ban, Banknote, CalendarDays, CheckCheck, Copy, ExternalLink, Loader2, MoreHorizontal, PenLine, RefreshCw, RotateCcw, SearchCheck, Stamp, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type RenewalOrderRow } from '../api/client'
import { inSentence, orderStatus } from './_status'
import { fmtDateTime, fmtMoney0, fmtMoney2, formatAbn } from './_utils'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { cn } from '@/lib/utils'

/*
 * What staff can do to paid renewals, in one place, for the list (one row, or many ticked) and the
 * renewal's own page. Each reason a renewal stopped has its actions, the first being the one that
 * usually clears it; every place shows the same ones in the same order, with the same dialogs.
 */

export type OrderActionKey = 'retry' | 'check' | 'findDue' | 'fixName' | 'setDue' | 'elsewhere' | 'registerAgain' | 'cancelled' | 'refund' | 'reopen'

/** How many of a renewal's actions show as buttons; the rest go in the "⋯" menu. */
const VISIBLE = { row: 2, page: 3 } as const

type ActionSpec = {
  label: string
  icon: ReactNode
  /** Can be done to many renewals at once (the list's bulk bar). */
  bulk: boolean
  /** On a list row, where room is short. */
  short?: string
  /** Shown in a menu, after a line. */
  destructive?: boolean
}

export const ORDER_ACTIONS: Record<OrderActionKey, ActionSpec> = {
  retry: { label: 'Try again', icon: <RefreshCw />, bulk: true },
  check: { label: 'Check ASIC now', short: 'Check ASIC', icon: <SearchCheck />, bulk: true },
  findDue: { label: 'Find due date at ASIC', short: 'Find due date', icon: <SearchCheck />, bulk: true },
  fixName: { label: 'Correct the name', icon: <PenLine />, bulk: false },
  setDue: { label: 'Set due date', icon: <CalendarDays />, bulk: false },
  elsewhere: { label: 'Renewed elsewhere', icon: <CheckCheck />, bulk: true },
  registerAgain: { label: 'Register it again', short: 'Register again', icon: <Stamp />, bulk: true },
  cancelled: { label: 'Client cancelled', icon: <Ban />, bulk: true },
  reopen: { label: 'Undo decision', icon: <RotateCcw />, bulk: true },
  refund: { label: 'Refund…', short: 'Refund', icon: <Banknote />, bulk: false, destructive: true },
}

/** The person outcomes an action records. */
const OUTCOME_OF: Partial<Record<OrderActionKey, string>> = { elsewhere: 'RenewedElsewhere', registerAgain: 'RegisterAgain', cancelled: 'ClientCancelling' }

/**
 * What a renewal offers now, in the order operators reach for them: the first two show on a list
 * row, the first three on the renewal's page, the rest in the "⋯" menu. `decided`: a person or the
 * ASIC check recorded an outcome (only then can it be undone); on a list row, a closed renewal
 * counts as decided.
 */
export function actionsFor(o: RenewalOrderRow, decided = o.stage === 'Closed'): OrderActionKey[] {
  const refund: OrderActionKey[] = o.source !== 'Manual' && o.stage !== 'Renewing' ? ['refund'] : []
  const undo: OrderActionKey[] = decided ? ['reopen'] : []
  const ontraport = o.source === 'Ontraport'
  const only = <T,>(yes: boolean, a: T): T[] => (yes ? [a] : [])
  switch (o.stage) {
    case 'Renewing': return []
    case 'Renewed': return refund
    case 'Closed': return [...undo, ...refund]
    case 'Due': return ['check', ...refund]
    case 'Waiting': return ['check', 'elsewhere', ...refund, ...undo]
  }
  switch (o.reason) {
    // ASIC's register knows the date; typing it in is the fallback.
    case 'NoDueDate': return [...only(ontraport, 'findDue' as const), ...only(ontraport, 'setDue' as const), ...refund, 'elsewhere']
    // Most were renewed by hand, or get their money back.
    case 'TooLate': return ['check', 'elsewhere', ...refund, 'registerAgain']
    case 'Lapsed': return ['registerAgain', ...refund, 'check', ...undo]
    case 'BelowMinimumFee': return ['elsewhere', ...refund, 'cancelled']
    case 'Ineligible': return [...refund, 'cancelled', 'elsewhere']
    case 'NameNotFound':
    case 'NameMismatch': return [...only(ontraport, 'fixName' as const), 'check', 'elsewhere', ...refund, ...undo]
    case 'CardDeclined':
    case 'AttemptsExhausted':
    case 'InProgressAtAsic': return [...only(o.canRetry, 'retry' as const), 'check', 'elsewhere', ...refund]
    // ASIC may hold the money: look first.
    default: return ['check', 'elsewhere', ...refund, ...undo]
  }
}

const ASIC_SEARCH = 'https://connectonline.asic.gov.au/RegistrySearch/faces/landing/SearchRegisters.jspx'

/** What each problem tab means and how to clear it, for the box above its list. */
export const TAB_GUIDE: Record<string, { title: string; reasons: string[]; steps: ReactNode[] }> = {
  'to-fix': {
    title: 'Paid, not renewed: stopped for a person',
    reasons: ['CardDeclined', 'AttemptsExhausted', 'InProgressAtAsic', 'VerifyPaymentAtAsic', 'NameNotFound', 'NameMismatch', 'AsicRefused', 'NoDueDate'],
    steps: [
      <><b>Card declined or ASIC didn’t answer:</b> tick them and <b>Try again</b>.</>,
      <><b>Check payment at ASIC:</b> ASIC may already hold the money. Look first, then mark it renewed elsewhere, or try again.</>,
      <><b>Name not found:</b> correct the name as ASIC shows it, and it goes back in the queue.</>,
    ],
  },
  decide: {
    title: 'Paid, but it can’t be renewed as it is',
    reasons: ['BelowMinimumFee', 'Ineligible', 'TooLate', 'Lapsed'],
    steps: [
      <>Each row says why. Open the <b>Too late</b> tab to work those together.</>,
      <>Renewed by hand at ASIC? Mark it <b>renewed elsewhere</b> and the payment is kept.</>,
      <>Otherwise <b>refund</b>, or offer to <b>register it again</b>.</>,
    ],
  },
  TooLate: {
    title: 'More than 30 days past due: ASIC won’t renew these online',
    reasons: ['TooLate'],
    steps: [
      <><b>Check ASIC now</b> (tick up to 25): if ASIC shows a later due date, someone renewed it, and it closes as renewed elsewhere.</>,
      <>Renewed by hand? Tick them and mark <b>renewed elsewhere</b>.</>,
      <>Still overdue or cancelled: offer to <b>register it again</b> (the payment goes towards it), or <b>refund</b>.</>,
    ],
  },
  NoDueDate: {
    title: 'No due date: neither Ontraport nor our records had one',
    reasons: ['NoDueDate'],
    steps: [
      <>Each morning’s run reads these from ASIC’s register on its own. To do it now, tick them (up to 25) and <b>Find due date</b>.</>,
      <>Once found, each renews, waits for the window, or moves to Too late on its own.</>,
      <>ASIC doesn’t list the name under the ABN? Correct the name, <b>Refund</b>, or <b>Set due date</b> by hand (<a className="text-link hover:underline" href={ASIC_SEARCH} target="_blank" rel="noreferrer">ASIC search</a>).</>,
    ],
  },
}

/** The box above a problem tab: what it is, how much is waiting, how to clear it. */
export function TabGuide({ view, byReason }: { view: string; byReason?: Array<{ reason: string; count: number; amount: number }> }) {
  const guide = TAB_GUIDE[view]
  if (!guide) return null
  const rows = (byReason ?? []).filter((r) => guide.reasons.includes(r.reason))
  const count = rows.reduce((n, r) => n + r.count, 0)
  const amount = rows.reduce((n, r) => n + r.amount, 0)
  return (
    <Card className="mb-4 gap-2 border-destructive/30 bg-destructive/5 px-4 py-3 sm:px-5">
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <h2 className="text-sm font-semibold">{guide.title}</h2>
        {byReason ? <span className="text-caption text-muted-foreground tabular-nums">{count.toLocaleString()} waiting · {fmtMoney0(amount)} paid</span> : null}
      </div>
      <ol className="list-decimal space-y-0.5 pl-5 text-sm text-muted-foreground marker:text-muted-foreground/70">
        {guide.steps.map((s, i) => <li key={i}>{s}</li>)}
      </ol>
    </Card>
  )
}

/* ─────────────────── Running an action ─────────────────── */

type Start = (action: OrderActionKey, orders: RenewalOrderRow[]) => void
const StartAction = createContext<Start | null>(null)

function useStart(): Start {
  const start = useContext(StartAction)
  if (!start) throw new Error('Order actions need an <OrderActionsProvider>')
  return start
}

type Skipped = Array<{ id: string; businessName: string; why: string }>

/**
 * Runs order actions for everything inside it and holds their dialogs, once per page. An action on
 * one renewal uses its own endpoint; on several, the bulk one (one refresh, skipped ones listed).
 */
export function OrderActionsProvider({ children, onDone }: { children: ReactNode; onDone?: () => void }) {
  const queryClient = useQueryClient()
  const [dialog, setDialog] = useState<{ action: OrderActionKey; orders: RenewalOrderRow[] } | null>(null)
  const [skipped, setSkipped] = useState<Skipped | null>(null)
  const [busy, setBusy] = useState(false)

  const refresh = () => {
    void queryClient.invalidateQueries({ predicate: (q) => typeof q.queryKey[0] === 'string' && q.queryKey[0].startsWith('admin-') })
    onDone?.()
  }

  const runNow = (action: 'retry' | 'check' | 'findDue' | 'reopen', orders: RenewalOrderRow[]) => {
    const one = orders.length === 1 ? orders[0] : null
    const doing = { retry: 'Trying again…', check: 'Asking ASIC…', findDue: 'Reading the due date at ASIC…', reopen: 'Undoing…' }[action]
    setBusy(true)
    const call: Promise<{ title: string; description?: string; skipped?: Skipped }> = action === 'findDue'
      // One or many: the bulk endpoint asks ASIC once per ABN. For one, a miss is said, not listed.
      ? api.admin.bulkRenewalOrders({ ids: orders.map((o) => o.id), action: 'find-due' }).then((r) => one && r.skipped.length
        ? Promise.reject(new Error(r.skipped[0].why))
        : { title: one ? 'Due date found at ASIC' : r.message, skipped: one ? undefined : r.skipped })
      : one
      ? action === 'retry' ? api.admin.retryRenewalOrder(one.id).then((r) => ({ title: r.message }))
        : action === 'reopen' ? api.admin.reopenRenewalOrder(one.id).then((r) => ({ title: r.message }))
        : api.admin.checkRenewalOrderAtAsic(one.id).then((r) => ({
          title: r.outcome ? `ASIC check: ${inSentence(orderStatus('NeedsPerson', r.outcome).label)}` : 'ASIC check done', description: r.evidence,
        }))
      : api.admin.bulkRenewalOrders({ ids: orders.map((o) => o.id), action }).then((r) => ({ title: r.message, skipped: r.skipped }))
    void sileo.promise(call, {
      loading: { title: orders.length > 1 ? `${doing} (${orders.length})` : doing },
      success: (r) => ({ title: r.title, description: r.description }),
      error: (e) => ({ title: 'Didn’t go through', description: e instanceof Error ? e.message : undefined }),
    }).then((r) => { if (r.skipped?.length) setSkipped(r.skipped) }).catch(() => {}).finally(() => { setBusy(false); refresh() })
  }

  const start: Start = (action, orders) => {
    if (orders.length === 0 || busy) return
    if (action === 'retry' || action === 'check' || action === 'findDue' || action === 'reopen') runNow(action, orders)
    else setDialog({ action, orders })
  }

  const close = () => setDialog(null)
  const one = dialog?.orders[0]
  return (
    <StartAction.Provider value={start}>
      {children}
      {dialog && OUTCOME_OF[dialog.action] ? (
        <DecideDialog outcome={OUTCOME_OF[dialog.action]!} orders={dialog.orders} onClose={close} onDone={refresh} onSkipped={setSkipped} />
      ) : null}
      {dialog?.action === 'refund' && one ? <RefundDialog order={one} onClose={close} onDone={refresh} /> : null}
      {dialog?.action === 'fixName' && one ? <FixNameDialog order={one} onClose={close} onDone={refresh} /> : null}
      {dialog?.action === 'setDue' && one ? <SetDueDialog order={one} onClose={close} onDone={refresh} /> : null}
      <SkippedDialog skipped={skipped} onClose={() => setSkipped(null)} />
    </StartAction.Provider>
  )
}

/* ─────────────────── Buttons ─────────────────── */

/**
 * A renewal's actions as buttons. `row`: the main one as a small button and the rest in a menu.
 * `page`: the first two as buttons (the first filled), the rest in a "More" menu.
 */
export function OrderActionButtons({ order, actions, layout = 'row', extra }: {
  order: RenewalOrderRow
  actions?: OrderActionKey[]
  layout?: 'row' | 'page'
  /** Links that go with them on the page (open the sale…), placed after the buttons. */
  extra?: ReactNode
}) {
  const start = useStart()
  const list = actions ?? actionsFor(order)
  if (list.length === 0 && !extra) return null
  const shown = VISIBLE[layout]
  const main = list.slice(0, shown)
  const rest = list.slice(shown)
  return (
    <div className="flex flex-wrap items-center gap-2">
      {main.map((a, i) => (
        <Button key={a} size="sm" variant={i === 0 ? (layout === 'row' ? 'outline' : 'default') : 'outline'}
          className={cn(layout === 'row' && 'h-7 px-2.5 text-xs')} onClick={() => start(a, [order])}>
          {ORDER_ACTIONS[a].icon}{layout === 'row' ? ORDER_ACTIONS[a].short ?? ORDER_ACTIONS[a].label : ORDER_ACTIONS[a].label}
        </Button>
      ))}
      {extra}
      {rest.length > 0 ? (
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button size="sm" variant="ghost" className={cn(layout === 'row' && 'size-7 p-0')} aria-label="More actions">
              <MoreHorizontal />{layout === 'page' ? 'More' : null}
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            {rest.map((a, i) => (
              <Fragment key={a}>
                {ORDER_ACTIONS[a].destructive && i > 0 ? <DropdownMenuSeparator /> : null}
                <DropdownMenuItem variant={ORDER_ACTIONS[a].destructive ? 'destructive' : 'default'} onSelect={() => start(a, [order])}>
                  {ORDER_ACTIONS[a].icon}{ORDER_ACTIONS[a].label}
                </DropdownMenuItem>
              </Fragment>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>
      ) : null}
    </div>
  )
}

/**
 * The bar for ticked rows: how many, how much, and only the actions every one of them offers and
 * that work on many at once.
 */
export function BulkBar({ orders, onClear }: { orders: RenewalOrderRow[]; onClear: () => void }) {
  const start = useStart()
  if (orders.length === 0) return null
  const lists = orders.map((o) => actionsFor(o))
  const shared = lists[0].filter((a) => ORDER_ACTIONS[a].bulk && lists.every((l) => l.includes(a)))
    .filter((a) => (a !== 'check' && a !== 'findDue') || orders.length <= 25)
  const amount = orders.reduce((n, o) => n + o.amountPaid, 0)
  return (
    <div className="sticky bottom-4 z-30 mt-4 flex flex-wrap items-center gap-2 rounded-lg border bg-popover px-3 py-2 shadow-lg">
      <span className="text-sm font-medium tabular-nums">{orders.length} selected</span>
      <span className="text-caption text-muted-foreground tabular-nums">· {fmtMoney0(amount)}</span>
      <div className="ml-auto flex flex-wrap items-center gap-2">
        {shared.length === 0
          ? <span className="text-caption text-muted-foreground">These don’t share an action that can be done together. Open each one.</span>
          : shared.map((a) => (
            <Button key={a} size="sm" variant="outline" onClick={() => start(a, orders)}>{ORDER_ACTIONS[a].icon}{ORDER_ACTIONS[a].label}</Button>
          ))}
        {lists.some((l) => l.includes('check') || l.includes('findDue')) && orders.length > 25
          ? <span className="text-caption text-muted-foreground">Asking ASIC: up to 25 at once</span> : null}
        <Button size="sm" variant="ghost" onClick={onClear} aria-label="Clear selection"><X />Clear</Button>
      </div>
    </div>
  )
}

/* ─────────────────── Dialogs ─────────────────── */

const OUTCOMES: Record<string, { label: string; ask: string; placeholder: string; reference?: string }> = {
  RenewedElsewhere: { label: 'Renewed elsewhere', ask: 'What shows it was renewed: what ASIC showed, or what the client said. The payment is kept.', placeholder: 'ASIC shows due 3 Mar 2027; renewed by hand…' },
  RegisterAgain: { label: 'Register it again', ask: 'What the client was told. The payment is kept towards the new registration.', placeholder: 'Emailed the client the registration link…' },
  ClientCancelling: { label: 'Client cancelled', ask: 'What the client asked for.', placeholder: 'Client doesn’t want it renewed…' },
}

function Names({ orders }: { orders: RenewalOrderRow[] }) {
  if (orders.length === 1) return <>{orders[0].businessName}</>
  return <>{orders.length} renewals ({orders.slice(0, 3).map((o) => o.businessName).join(', ')}{orders.length > 3 ? `, and ${orders.length - 3} more` : ''})</>
}

function DecideDialog({ outcome, orders, onClose, onDone, onSkipped }: {
  outcome: string; orders: RenewalOrderRow[]; onClose: () => void; onDone: () => void; onSkipped: (s: Skipped) => void
}) {
  const [note, setNote] = useState('')
  const spec = OUTCOMES[outcome]
  const save = useMutation({
    mutationFn: async () => orders.length === 1
      ? { message: (await api.admin.setRenewalOrderOutcome(orders[0].id, { outcome, note })).message, skipped: [] as Skipped }
      : api.admin.bulkRenewalOrders({ ids: orders.map((o) => o.id), action: 'outcome', outcome, note }),
  })
  const registerLink = `${window.location.origin}/register`
  const submit = () => void sileo.promise(save.mutateAsync(), {
    loading: { title: 'Saving…' },
    success: (r) => ({ title: orders.length === 1 ? `Marked ${inSentence(spec.label)}` : r.message }),
    error: (e) => ({ title: 'Not saved', description: e instanceof Error ? e.message : undefined }),
  }).then((r) => { if (r.skipped.length) onSkipped(r.skipped); onDone(); onClose() }).catch(() => {})

  return (
    <Dialog open onOpenChange={(o) => { if (!o && !save.isPending) onClose() }}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{spec.label}: <Names orders={orders} /></DialogTitle>
          <DialogDescription>{spec.ask}{orders.length > 1 ? ' The same note goes on each.' : ''}</DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          {outcome === 'RegisterAgain' ? (
            <div className="flex items-center gap-2 rounded-md border bg-muted/40 px-3 py-2 text-sm">
              <span className="min-w-0 flex-1 truncate">Send the client: <span className="font-mono">{registerLink}</span></span>
              <Button size="sm" variant="ghost" onClick={() => void navigator.clipboard.writeText(registerLink).then(() => sileo.success({ title: 'Link copied' }))}><Copy />Copy</Button>
            </div>
          ) : null}
          <div className="grid gap-1.5">
            <Label htmlFor="decide-note">Note</Label>
            <Textarea id="decide-note" value={note} onChange={(e) => setNote(e.target.value)} rows={3} autoFocus placeholder={spec.placeholder} />
          </div>
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>Cancel</Button>
          <Button onClick={submit} disabled={save.isPending || !note.trim()}>
            {save.isPending ? <Loader2 className="animate-spin" /> : null}{orders.length > 1 ? `Save for ${orders.length}` : 'Save'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function SetDueDialog({ order, onClose, onDone }: { order: RenewalOrderRow; onClose: () => void; onDone: () => void }) {
  const [due, setDue] = useState(order.dueDate?.slice(0, 10) ?? '')
  const save = useMutation({ mutationFn: () => api.admin.setRenewalOrderDueDate(order.id, due) })
  const submit = () => void sileo.promise(save.mutateAsync(), {
    loading: { title: 'Saving…' },
    success: (r) => ({ title: 'Due date set', description: r.message }),
    error: (e) => ({ title: 'Not saved', description: e instanceof Error ? e.message : undefined }),
  }).then(() => { onDone(); onClose() }).catch(() => {})
  const copy = (text: string) => void navigator.clipboard.writeText(text).then(() => sileo.success({ title: 'Copied' }))
  return (
    <Dialog open onOpenChange={(o) => { if (!o && !save.isPending) onClose() }}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Set the due date: {order.businessName}</DialogTitle>
          <DialogDescription>
            Find the name’s renewal date at ASIC. Once saved, it renews, waits for ASIC’s window, or moves to Too late on its own.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          <div className="space-y-1.5 rounded-md border bg-muted/40 px-3 py-2 text-sm">
            <div className="flex items-center gap-2">
              <span className="min-w-0 flex-1 truncate">ABN <span className="font-mono">{formatAbn(order.abn)}</span></span>
              <Button size="sm" variant="ghost" className="h-7" onClick={() => copy(order.abn)}><Copy />Copy</Button>
            </div>
            <div className="flex items-center gap-2">
              <span className="min-w-0 flex-1 truncate">{order.businessName}</span>
              <Button size="sm" variant="ghost" className="h-7" onClick={() => copy(order.businessName)}><Copy />Copy</Button>
            </div>
            <a href={ASIC_SEARCH} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-link hover:underline">
              Open ASIC search<ExternalLink className="size-3.5" />
            </a>
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="due-date">Renewal due date</Label>
            <Input id="due-date" type="date" value={due} onChange={(e) => setDue(e.target.value)} autoFocus className="w-48" />
          </div>
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>Cancel</Button>
          <Button onClick={submit} disabled={save.isPending || !due}>{save.isPending ? <Loader2 className="animate-spin" /> : null}Save</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function FixNameDialog({ order, onClose, onDone }: { order: RenewalOrderRow; onClose: () => void; onDone: () => void }) {
  const [name, setName] = useState(order.businessName)
  const save = useMutation({ mutationFn: () => api.admin.fixRenewalOrderName(order.id, name) })
  const submit = () => void sileo.promise(save.mutateAsync(), {
    loading: { title: 'Saving…' },
    success: (r) => ({ title: r.message }),
    error: (e) => ({ title: 'Name not changed', description: e instanceof Error ? e.message : undefined }),
  }).then(() => { onDone(); onClose() }).catch(() => {})
  return (
    <Dialog open onOpenChange={(o) => { if (!o && !save.isPending) onClose() }}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Correct the business name</DialogTitle>
          <DialogDescription>Type it as ASIC shows it under ABN {formatAbn(order.abn)}. The sale goes back in the queue and renews on the next run.</DialogDescription>
        </DialogHeader>
        <DialogBody>
          <div className="grid gap-1.5">
            <Label htmlFor="fix-name">Business name</Label>
            <Input id="fix-name" value={name} onChange={(e) => setName(e.target.value)} autoFocus />
          </div>
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>Cancel</Button>
          <Button onClick={submit} disabled={save.isPending || !name.trim() || name.trim() === order.businessName}>{save.isPending ? 'Saving…' : 'Save and requeue'}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/**
 * Refunds one payment behind a renewal to the customer's card, through where they paid (Ontraport
 * or Stripe). Lists every payment and why one can't be refunded; nothing moves until the person
 * picks one and confirms. One renewal at a time: each refund is a payment someone chooses.
 */
function RefundDialog({ order, onClose, onDone }: { order: RenewalOrderRow; onClose: () => void; onDone: () => void }) {
  const [picked, setPicked] = useState<string | null>(null)
  const [note, setNote] = useState('')
  const options = useQuery({ queryKey: ['admin-renewal-order-refund', order.id], queryFn: () => api.admin.renewalOrderRefundOptions(order.id), staleTime: 0 })
  const refund = useMutation({ mutationFn: () => api.admin.refundRenewalOrder(order.id, { payment: picked!, note: note.trim() || undefined }) })
  const payments = options.data?.payments ?? []
  const choice = payments.find((p) => p.key === picked && p.refundable)
    ?? (payments.filter((p) => p.refundable).length === 1 ? payments.find((p) => p.refundable) : undefined)
  const who = order.clientName || order.payerName || 'the client'
  const submit = () => {
    if (!choice) return
    setPicked(choice.key)
    void sileo.promise(refund.mutateAsync(), {
      loading: { title: `Refunding ${fmtMoney2(choice.amount)}…` },
      success: (r) => ({ title: r.message, description: r.reference }),
      error: (e) => ({ title: 'Not refunded', description: e instanceof Error ? e.message : undefined }),
    }).then(() => { onDone(); onClose() }).catch(() => { void options.refetch() })
  }

  return (
    <Dialog open onOpenChange={(o) => { if (!o && !refund.isPending) onClose() }}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Refund {who}: {order.businessName}</DialogTitle>
          <DialogDescription>
            The money goes back to their card through where they paid. This can’t be undone.
            {order.stage === 'Renewed' ? ' This name was renewed: refund only a payment made twice or by mistake.' : ''}
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          {options.isPending ? <p className="text-sm text-muted-foreground"><Loader2 className="mr-2 inline size-4 animate-spin" />Looking up their payments…</p>
            : options.isError ? <p className="text-sm text-destructive">The payments couldn’t be loaded. Try again.</p>
            : (
              <div className="space-y-2">
                {options.data?.note ? <p className="text-caption text-muted-foreground">{options.data.note}</p> : null}
                {payments.map((p) => (
                  <label key={p.key} className={cn('flex items-start gap-3 rounded-md border px-3 py-2 text-sm', p.refundable ? 'cursor-pointer hover:bg-muted/50' : 'opacity-60', choice?.key === p.key && 'border-primary bg-accent/60')}>
                    <input type="radio" name="refund-payment" className="mt-1" disabled={!p.refundable || refund.isPending}
                      checked={choice?.key === p.key} onChange={() => setPicked(p.key)} />
                    <span className="min-w-0 flex-1">
                      <span className="font-medium tabular-nums">{fmtMoney2(p.amount)}</span> · {p.label}
                      {p.paidAt ? <span className="text-muted-foreground"> · paid {fmtDateTime(p.paidAt)}</span> : null}
                      {p.why ? <span className="mt-0.5 block text-caption text-muted-foreground">{p.why}</span> : null}
                    </span>
                  </label>
                ))}
              </div>
            )}
          <div className="grid gap-1.5">
            <Label htmlFor="refund-note">Why (kept on the renewal)</Label>
            <Textarea id="refund-note" value={note} onChange={(e) => setNote(e.target.value)} rows={2} placeholder="Paid twice; not renewed in time; client asked…" />
          </div>
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={refund.isPending}>Cancel</Button>
          <Button variant="destructive" onClick={submit} disabled={!choice || refund.isPending}>
            {refund.isPending ? <Loader2 className="animate-spin" /> : null}
            {choice ? `Refund ${fmtMoney2(choice.amount)}` : 'Refund'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** After a bulk action: the renewals it didn't apply to, and why, so none is lost. */
function SkippedDialog({ skipped, onClose }: { skipped: Skipped | null; onClose: () => void }) {
  return (
    <Dialog open={!!skipped && skipped.length > 0} onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{skipped?.length} skipped</DialogTitle>
          <DialogDescription>The rest went through. These need a look on their own.</DialogDescription>
        </DialogHeader>
        <DialogBody>
          <ul className="max-h-80 space-y-2 overflow-y-auto text-sm">
            {skipped?.map((s) => (
              <li key={s.id}>
                <a href={`/admin/renewal-orders/${s.id}`} target="_blank" rel="noreferrer" className="font-medium hover:underline">{s.businessName}</a>
                <div className="text-caption text-muted-foreground">{s.why}</div>
              </li>
            ))}
          </ul>
        </DialogBody>
        <DialogFooter><Button onClick={onClose}>OK</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
