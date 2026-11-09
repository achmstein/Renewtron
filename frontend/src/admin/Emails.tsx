import { useEffect, useRef, useState, type ReactNode, type Ref } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, ArrowLeft, CheckCheck, Eye, Inbox, LayoutTemplate, Loader2, Mail, MailOpen, RotateCw, Search, Send, Tag } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type EmailDetail, type EmailListResponse, type EmailRow } from '../api/client'
import { Status, statusOf, type StatusEntry } from './_status'
import { fmtDateTime, relativeTime } from './_utils'
import { RefreshButton } from './_ui'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { cn } from '@/lib/utils'

/*
 * Emails (/admin/emails): every email Renewtron sent or tried to, as a full-screen mail client.
 * On a wide screen, three panes beside the nav (folded to icons on this page): mailboxes (by
 * status, then by kind), the list (newest first, searchable), and the email as the customer saw
 * it, with SendGrid's answer and what happened at the inbox. ↑/↓ or j/k move through the list.
 * On a phone or narrow screen: the list (mailboxes as dropdowns), then the email.
 */

const FOLDERS = [
  { key: 'all', label: 'All', icon: <Inbox /> },
  { key: 'problem', label: 'Problems', icon: <AlertTriangle /> },
  { key: 'sent', label: 'Sent', icon: <Send /> },
  { key: 'delivered', label: 'Delivered', icon: <CheckCheck /> },
  { key: 'opened', label: 'Opened', icon: <MailOpen /> },
] as const
type FolderKey = (typeof FOLDERS)[number]['key']
const ALL_KINDS = '_all'

/** "lead captured" → "Lead captured". */
const kindWords = (k: string) => (k ? k[0].toUpperCase() + k.slice(1) : k)

/** The status that matters most: not sent, else what happened at the inbox, else sent. */
function emailStatus(e: Pick<EmailRow, 'status' | 'deliveryStatus'>): StatusEntry {
  if (e.status !== 'Sent') return statusOf('email', e.status)
  return statusOf('email', e.deliveryStatus ?? 'Sent')
}

export default function Emails() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const tab = (params.get('tab') as FolderKey | null) ?? 'all'
  const kind = params.get('kind') ?? ''
  const [search, setSearch] = useState(params.get('q') ?? '')
  const [query, setQuery] = useState(search)
  const [page, setPage] = useState(1)

  // Search as you type, a moment after typing stops.
  useEffect(() => {
    const t = window.setTimeout(() => { setQuery(search.trim()); setPage(1) }, 300)
    return () => window.clearTimeout(t)
  }, [search])

  const list = useQuery({
    queryKey: ['admin-emails', tab, kind, query, page],
    queryFn: () => api.admin.emails({ tab, kind, search: query, page, pageSize: 50 }),
    placeholderData: keepPreviousData,
    refetchInterval: 60_000,
  })
  const data = list.data
  const keep = (to: string) => `${to}${params.toString() ? `?${params}` : ''}`
  const openId = (rowId: string) => navigate(keep(`/admin/emails/${rowId}`))
  const setParam = (name: string, value: string) => {
    setPage(1)
    setParams((p) => { if (value) p.set(name, value); else p.delete(name); return p }, { replace: true })
  }
  const setTab = (t: FolderKey) => setParam('tab', t === 'all' ? '' : t)
  const setKind = (k: string) => setParam('kind', k)
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1

  // ↑/↓ or j/k: the next or previous email in the list (not while typing).
  const items = data?.items
  useEffect(() => {
    const onKey = (ev: KeyboardEvent) => {
      if (ev.ctrlKey || ev.metaKey || ev.altKey || !items?.length) return
      const el = ev.target as HTMLElement | null
      if (el && (el.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(el.tagName) || el.closest('[role="listbox"],[role="dialog"]'))) return
      const step = ev.key === 'ArrowDown' || ev.key === 'j' ? 1 : ev.key === 'ArrowUp' || ev.key === 'k' ? -1 : 0
      if (!step) return
      ev.preventDefault()
      const at = items.findIndex((r) => r.id === id)
      const next = items[at < 0 ? 0 : Math.min(items.length - 1, Math.max(0, at + step))]
      if (next && next.id !== id) openId(next.id)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })

  return (
    <div className="flex lg:h-svh">
      {/* ── Mailboxes (wide screens) ── */}
      <aside className="hidden w-52 shrink-0 flex-col border-r bg-muted/30 lg:flex">
        <div className="flex items-center justify-between gap-2 px-4 pt-5 pb-3">
          <h1 className="text-heading">Emails</h1>
          <RefreshButton onClick={() => void list.refetch()} busy={list.isFetching} />
        </div>
        <nav className="min-h-0 flex-1 space-y-5 overflow-y-auto px-2 pb-4">
          <div className="space-y-0.5">
            {FOLDERS.map((f) => (
              <MailboxItem
                key={f.key}
                icon={f.icon}
                label={f.label}
                active={tab === f.key}
                count={data?.counts[f.key]}
                alarm={f.key === 'problem' && !!data?.counts.problem}
                onClick={() => setTab(f.key)}
              />
            ))}
          </div>
          {data?.kinds.length ? (
            <div>
              <div className="px-2.5 pb-1 text-caption font-medium text-muted-foreground">By kind</div>
              <div className="space-y-0.5">
                <MailboxItem icon={<Tag />} label="Every kind" active={!kind} onClick={() => setKind('')} />
                {data.kinds.map((k) => (
                  <MailboxItem key={k} icon={<Tag />} label={kindWords(k)} active={kind === k} onClick={() => setKind(k)} />
                ))}
              </div>
            </div>
          ) : null}
        </nav>
        <div className="border-t p-2">
          <Link to="/admin/emails/templates" className="flex items-center gap-2.5 rounded-md px-2.5 py-1.5 text-sm text-muted-foreground transition-colors hover:bg-muted hover:text-foreground [&_svg]:size-4">
            <LayoutTemplate /> Templates
          </Link>
        </div>
      </aside>

      {/* ── The list ── */}
      <section className={cn('flex w-full min-w-0 flex-col border-r lg:w-[22rem] lg:shrink-0 xl:w-[26rem]', id && 'hidden lg:flex')}>
        <div className="space-y-3 border-b p-3">
          <div className="flex items-center justify-between gap-2 lg:hidden">
            <h1 className="text-title">Emails</h1>
            <div className="flex items-center gap-1">
              <Button asChild variant="ghost" size="sm"><Link to="/admin/emails/templates"><LayoutTemplate /> Templates</Link></Button>
              <RefreshButton onClick={() => void list.refetch()} busy={list.isFetching} />
            </div>
          </div>
          <div className="grid grid-cols-2 gap-2 lg:hidden">
            <Select value={tab} onValueChange={(v) => setTab(v as FolderKey)}>
              <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                {FOLDERS.map((f) => (
                  <SelectItem key={f.key} value={f.key}>{f.label}{data ? ` (${data.counts[f.key].toLocaleString()})` : ''}</SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Select value={kind || ALL_KINDS} onValueChange={(v) => setKind(v === ALL_KINDS ? '' : v)}>
              <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL_KINDS}>Every kind</SelectItem>
                {data?.kinds.map((k) => <SelectItem key={k} value={k}>{kindWords(k)}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <div className="relative">
            <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input className="pl-8" placeholder="Search by address or subject" value={search} onChange={(e) => setSearch(e.target.value)} />
          </div>
        </div>

        {data?.problem ? <ProblemBanner problem={data.problem} /> : null}

        <EmailList list={list} data={data} query={query} tab={tab} selectedId={id} onOpen={openId} />

        {data && pages > 1 ? (
          <div className="flex items-center justify-between border-t px-3 py-2 text-caption text-muted-foreground">
            <span>Page {page} of {pages} · {data.total.toLocaleString()}</span>
            <div className="flex gap-1">
              <Button size="sm" variant="ghost" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>Newer</Button>
              <Button size="sm" variant="ghost" disabled={page >= pages} onClick={() => setPage((p) => p + 1)}>Older</Button>
            </div>
          </div>
        ) : null}
      </section>

      {/* ── The email ── */}
      <main className={cn('flex min-w-0 flex-1 flex-col bg-card', !id && 'hidden lg:flex')}>
        {id ? <EmailPane id={id} onBack={() => navigate(keep('/admin/emails'))} /> : (
          <div className="flex flex-1 items-center justify-center p-8 text-center text-sm text-muted-foreground">
            <div>
              <Mail className="mx-auto mb-2 size-6" />
              Pick an email to read it.
              <div className="mt-1 text-caption">↑ ↓ or j k to move through the list</div>
            </div>
          </div>
        )}
      </main>
    </div>
  )
}

function MailboxItem({ icon, label, active, count, alarm, onClick }: {
  icon: ReactNode; label: string; active: boolean; count?: number; alarm?: boolean; onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn('flex w-full items-center gap-2.5 rounded-md px-2.5 py-1.5 text-left text-sm transition-colors [&>svg]:size-4 [&>svg]:shrink-0',
        active ? 'bg-muted font-medium text-foreground' : 'text-muted-foreground hover:bg-muted/60 hover:text-foreground',
        alarm && '[&>svg]:text-destructive')}
    >
      {icon}
      <span className="min-w-0 flex-1 truncate">{label}</span>
      {count !== undefined ? (
        <span className={cn('tabular-nums text-caption', alarm ? 'font-semibold text-destructive' : 'text-muted-foreground')}>{count.toLocaleString()}</span>
      ) : null}
    </button>
  )
}

function ProblemBanner({ problem }: { problem: string }) {
  return (
    <div className="flex items-start gap-2.5 border-b border-destructive/20 bg-destructive/5 px-3 py-2.5 text-sm">
      <AlertTriangle className="mt-0.5 size-4 shrink-0 text-destructive" />
      <div className="min-w-0 flex-1">
        <div className="font-medium">Emails aren’t going out</div>
        <div className="mt-0.5 text-muted-foreground">{problem}</div>
      </div>
      <Button asChild size="sm" variant="outline"><Link to="/admin/settings">Settings</Link></Button>
    </div>
  )
}

function EmailList({ list, data, query, tab, selectedId, onOpen }: {
  list: { isPending: boolean }; data: EmailListResponse | undefined; query: string; tab: FolderKey
  selectedId: string | undefined; onOpen: (id: string) => void
}) {
  // Keep the open email in view as you move through the list with the keyboard.
  const selectedRef = useRef<HTMLButtonElement>(null)
  useEffect(() => { selectedRef.current?.scrollIntoView({ block: 'nearest' }) }, [selectedId])

  return (
    <div className="min-h-64 flex-1 divide-y lg:min-h-0 lg:overflow-y-auto">
      {list.isPending ? (
        <div className="p-8 text-center"><Loader2 className="mx-auto size-5 animate-spin text-muted-foreground" /></div>
      ) : !data || data.items.length === 0 ? (
        <div className="p-8 text-center text-sm text-muted-foreground">
          {query ? 'Nothing matches this search.' : tab === 'problem' ? 'No problems: every email went out.' : 'No emails here yet.'}
        </div>
      ) : (
        data.items.map((row) => {
          const selected = row.id === selectedId
          return <EmailListRow key={row.id} ref={selected ? selectedRef : undefined} row={row} selected={selected} onOpen={() => onOpen(row.id)} />
        })
      )}
    </div>
  )
}

function EmailListRow({ row, selected, onOpen, ref }: { row: EmailRow; selected: boolean; onOpen: () => void; ref?: Ref<HTMLButtonElement> }) {
  const st = emailStatus(row)
  return (
    <button
      ref={ref}
      type="button"
      onClick={onOpen}
      className={cn('block w-full border-l-2 border-transparent px-3 py-2.5 text-left transition-colors hover:bg-muted/60',
        selected && 'border-l-foreground bg-muted')}
    >
      <div className="flex items-center justify-between gap-2">
        <span className={cn('truncate text-sm', st.meaning === 'action' ? 'font-semibold' : 'font-medium')}>{row.to}</span>
        <span className="shrink-0 text-caption text-muted-foreground" title={fmtDateTime(row.createdAt)}>{relativeTime(row.createdAt)}</span>
      </div>
      <div className="mt-0.5 truncate text-sm text-muted-foreground">{row.subject || '(no subject)'}</div>
      <div className="mt-1.5 flex items-center gap-2">
        <Status entry={st} />
        {row.openedAt ? <span className="inline-flex items-center gap-1 text-caption text-muted-foreground"><Eye className="size-3" />opened</span> : null}
        <span className="truncate text-caption text-muted-foreground">{kindWords(row.kind)}</span>
      </div>
    </button>
  )
}

function EmailPane({ id, onBack }: { id: string; onBack: () => void }) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const { data: e, isPending, isError } = useQuery({ queryKey: ['admin-email', id], queryFn: () => api.admin.email(id) })
  const [asText, setAsText] = useState(false)
  const resend = useMutation({ mutationFn: () => api.admin.resendEmail(id) })

  if (isPending) return <div className="flex flex-1 items-center justify-center p-8"><Loader2 className="size-5 animate-spin text-muted-foreground" /></div>
  if (isError || !e) return <div className="flex flex-1 items-center justify-center p-8 text-sm text-muted-foreground">Email not found.</div>

  const st = emailStatus(e)
  const runResend = () => void sileo.promise(resend.mutateAsync(), {
    loading: { title: 'Sending it again…' },
    success: (r) => ({ title: r.message }),
    error: (err) => ({ title: 'Couldn’t send it', description: err instanceof Error ? err.message : undefined }),
  }).then((r) => {
    void queryClient.invalidateQueries({ queryKey: ['admin-emails'] })
    void queryClient.invalidateQueries({ queryKey: ['admin-email', id] })
    navigate(`/admin/emails/${r.id}`)
  }).catch(() => {})
  const canResend = !!(e.bodyHtml || e.bodyText)
  const sep = <span className="text-border">·</span>

  return (
    <>
      <div className="space-y-2.5 border-b px-4 py-3 sm:px-5">
        <button type="button" onClick={onBack} className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground lg:hidden">
          <ArrowLeft className="size-4" /> Emails
        </button>
        <div className="flex items-start justify-between gap-3">
          <h2 className="min-w-0 text-heading break-words">{e.subject || '(no subject)'}</h2>
          <Status entry={st} size="lg" />
        </div>

        <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-sm text-muted-foreground">
          <span>To <span className="text-foreground">{e.to}</span></span>
          {e.client ? <>{sep}<Link className="text-link hover:underline" to={`/admin/clients/${e.client.id}`}>{e.client.fullName || 'client'}</Link></> : null}
          {sep}<span>{kindWords(e.kind)}</span>
          {e.templateKey && e.templateVersion !== null ? <>{sep}<Link className="text-link hover:underline" to={`/admin/emails/templates/${encodeURIComponent(e.templateKey)}?history=${e.templateVersion}`} title="The template this email was written from">{e.templateVersion === 0 ? 'Default wording' : `Version ${e.templateVersion}`}</Link></> : null}
        </div>

        <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-caption text-muted-foreground">
          <span>{e.sentAt ? `Sent ${fmtDateTime(e.sentAt)}` : e.lastAttemptAt ? `Last try ${fmtDateTime(e.lastAttemptAt)}` : `Created ${fmtDateTime(e.createdAt)}, not sent`}{e.attemptCount > 1 ? ` (${e.attemptCount} tries)` : ''}</span>
          {e.deliveryAt ? <>{sep}<span>{statusOf('email', e.deliveryStatus).label} {fmtDateTime(e.deliveryAt)}</span></> : e.status === 'Sent' ? <>{sep}<span>No delivery report yet</span></> : null}
          {e.openedAt ? <>{sep}<span>Opened {fmtDateTime(e.openedAt)}</span></> : null}
          {e.providerMessageId ? <>{sep}<span className="max-w-60 truncate" title={`SendGrid message id ${e.providerMessageId}`}>SendGrid {e.providerMessageId}</span></> : null}
        </div>

        {e.error || e.deliveryDetail ? (
          <div className="rounded-md border border-destructive/20 bg-destructive/5 px-3 py-2 text-sm">
            <span className="font-medium">{e.status === 'Failed' ? 'SendGrid said: ' : 'The recipient’s server said: '}</span>
            <span className="break-words text-muted-foreground">{e.error ?? e.deliveryDetail}</span>
          </div>
        ) : null}

        {e.resends.length ? (
          <div className="text-caption text-muted-foreground">
            Sent again: {e.resends.map((r, i) => (
              <span key={r.id}>{i ? ', ' : ''}<Link className="text-link hover:underline" to={`/admin/emails/${r.id}`}>{fmtDateTime(r.createdAt)} ({statusOf('email', r.status).label.toLowerCase()})</Link></span>
            ))}
          </div>
        ) : null}

        <div className="flex flex-wrap items-center gap-2">
          <Button size="sm" variant={st.meaning === 'action' ? 'default' : 'outline'} onClick={runResend} disabled={!canResend || resend.isPending}
            title={canResend ? `Send this email to ${e.to} again` : 'Its text is no longer kept (older than 180 days)'}>
            {resend.isPending ? <Loader2 className="animate-spin" /> : <RotateCw />} Resend
          </Button>
          {e.bodyHtml && e.bodyText ? (
            <Button size="sm" variant="ghost" onClick={() => setAsText((v) => !v)}>{asText ? 'Show as sent' : 'Show plain text'}</Button>
          ) : null}
          {e.renewalRequestId ? <Button asChild size="sm" variant="ghost"><Link to={`/admin/renewals/${e.renewalRequestId}`}>Open renewal</Link></Button> : null}
          {e.leadId ? <Button asChild size="sm" variant="ghost"><Link to={`/admin/leads/${e.leadId}`}>Open lead</Link></Button> : null}
          {e.resendOfId ? <Button asChild size="sm" variant="ghost"><Link to={`/admin/emails/${e.resendOfId}`}>Earlier send</Link></Button> : null}
        </div>
      </div>

      <EmailBody detail={e} asText={asText} />
    </>
  )
}

/** The email as the customer saw it, in a locked-down frame (no scripts, no forms, links open a new tab). Fills the pane. */
function EmailBody({ detail: e, asText }: { detail: EmailDetail; asText: boolean }) {
  if (!e.bodyHtml && !e.bodyText) {
    return <div className="flex flex-1 items-center justify-center p-8 text-center text-sm text-muted-foreground">The text of this email is no longer kept (we keep it for 180 days).</div>
  }
  if (asText || !e.bodyHtml) {
    return <pre className="min-h-[60dvh] flex-1 overflow-y-auto whitespace-pre-wrap break-words p-4 text-sm sm:p-5 lg:min-h-0">{e.bodyText}</pre>
  }
  const doc = `<base target="_blank">${e.bodyHtml}`
  return <iframe title="Email" sandbox="allow-popups allow-popups-to-escape-sandbox" srcDoc={doc} className="h-[75dvh] w-full flex-1 bg-white lg:h-auto lg:min-h-0" />
}
