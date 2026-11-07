import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, ArrowLeft, Eye, Loader2, Mail, RotateCw, Search } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type EmailDetail, type EmailRow } from '../api/client'
import { Status, statusOf, type StatusEntry } from './_status'
import { fmtDateTime, relativeTime } from './_utils'
import { RefreshButton } from './_ui'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { cn } from '@/lib/utils'

/*
 * Emails (/admin/emails): every email Renewtron sent or tried to, as an inbox. The list on the
 * left (newest first, by tab and search), the email on the right as the customer saw it, with
 * SendGrid's answer and what happened at the inbox. On a phone: the list, then the email.
 */

const TABS = [
  { key: 'all', label: 'All' },
  { key: 'problem', label: 'Problems' },
  { key: 'sent', label: 'Sent' },
  { key: 'delivered', label: 'Delivered' },
  { key: 'opened', label: 'Opened' },
] as const
type TabKey = (typeof TABS)[number]['key']

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
  const tab = (params.get('tab') as TabKey | null) ?? 'all'
  const [search, setSearch] = useState(params.get('q') ?? '')
  const [query, setQuery] = useState(search)
  const [page, setPage] = useState(1)

  // Search as you type, a moment after typing stops.
  useEffect(() => {
    const t = window.setTimeout(() => { setQuery(search.trim()); setPage(1) }, 300)
    return () => window.clearTimeout(t)
  }, [search])

  const list = useQuery({
    queryKey: ['admin-emails', tab, query, page],
    queryFn: () => api.admin.emails({ tab, search: query, page, pageSize: 50 }),
    placeholderData: keepPreviousData,
    refetchInterval: 60_000,
  })
  const data = list.data
  const keep = (to: string) => `${to}${params.toString() ? `?${params}` : ''}`
  const open = (row: EmailRow) => navigate(keep(`/admin/emails/${row.id}`))
  const setTab = (t: TabKey) => { setPage(1); setParams((p) => { p.set('tab', t); return p }, { replace: true }) }
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1

  return (
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-title">Emails</h1>
          <p className="mt-1 text-sm text-muted-foreground">Everything Renewtron sends, and whether it got there.</p>
        </div>
        <RefreshButton onClick={() => void list.refetch()} busy={list.isFetching} />
      </div>

      {data?.problem ? (
        <div className="mb-4 flex items-start gap-3 rounded-lg border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm">
          <AlertTriangle className="mt-0.5 size-4 shrink-0 text-destructive" />
          <div className="min-w-0 flex-1">
            <div className="font-medium">Emails aren’t going out</div>
            <div className="mt-0.5 text-muted-foreground">{data.problem}</div>
          </div>
          <Button asChild size="sm" variant="outline"><Link to="/admin/settings">Settings</Link></Button>
        </div>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[minmax(0,26rem)_minmax(0,1fr)]">
        {/* ── The list ── */}
        <Card className={cn('gap-0 overflow-hidden py-0', id && 'hidden lg:flex')}>
          <div className="space-y-3 border-b p-3">
            <div className="relative">
              <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input className="pl-8" placeholder="Search by email address or subject" value={search} onChange={(e) => setSearch(e.target.value)} />
            </div>
            <div className="flex flex-wrap gap-1">
              {TABS.map((t) => (
                <button
                  key={t.key}
                  type="button"
                  onClick={() => setTab(t.key)}
                  className={cn('rounded-md px-2.5 py-1 text-sm transition-colors',
                    tab === t.key ? 'bg-foreground text-background' : 'text-muted-foreground hover:bg-muted hover:text-foreground')}
                >
                  {t.label}
                  {data ? <span className={cn('ml-1.5 tabular-nums', tab === t.key ? 'opacity-70' : '', t.key === 'problem' && data.counts.problem > 0 && tab !== t.key && 'text-destructive')}>{data.counts[t.key]}</span> : null}
                </button>
              ))}
            </div>
          </div>

          <div className="max-h-[calc(100dvh-16rem)] min-h-64 divide-y overflow-y-auto">
            {list.isPending ? (
              <div className="p-8 text-center"><Loader2 className="mx-auto size-5 animate-spin text-muted-foreground" /></div>
            ) : !data || data.items.length === 0 ? (
              <div className="p-8 text-center text-sm text-muted-foreground">
                {query ? 'Nothing matches this search.' : tab === 'problem' ? 'No problems: every email went out.' : 'No emails here yet.'}
              </div>
            ) : (
              data.items.map((row) => <EmailListRow key={row.id} row={row} selected={row.id === id} onOpen={() => open(row)} />)
            )}
          </div>

          {data && pages > 1 ? (
            <div className="flex items-center justify-between border-t px-3 py-2 text-caption text-muted-foreground">
              <span>Page {page} of {pages} · {data.total.toLocaleString()} emails</span>
              <div className="flex gap-1">
                <Button size="sm" variant="ghost" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>Newer</Button>
                <Button size="sm" variant="ghost" disabled={page >= pages} onClick={() => setPage((p) => p + 1)}>Older</Button>
              </div>
            </div>
          ) : null}
        </Card>

        {/* ── The email ── */}
        <div className={cn(!id && 'hidden lg:block')}>
          {id ? <EmailPane id={id} onBack={() => navigate(keep('/admin/emails'))} /> : (
            <Card className="flex h-full min-h-64 items-center justify-center p-8 text-center">
              <div className="text-sm text-muted-foreground">
                <Mail className="mx-auto mb-2 size-6" />
                Pick an email to read it.
              </div>
            </Card>
          )}
        </div>
      </div>
    </div>
  )
}

function EmailListRow({ row, selected, onOpen }: { row: EmailRow; selected: boolean; onOpen: () => void }) {
  const st = emailStatus(row)
  return (
    <button
      type="button"
      onClick={onOpen}
      className={cn('block w-full px-3 py-2.5 text-left transition-colors hover:bg-muted/60', selected && 'bg-muted')}
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

  if (isPending) return <Card className="p-8 text-center"><Loader2 className="mx-auto size-5 animate-spin text-muted-foreground" /></Card>
  if (isError || !e) return <Card className="p-8 text-center text-sm text-muted-foreground">Email not found.</Card>

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

  return (
    <Card className="gap-0 overflow-hidden py-0">
      <div className="space-y-3 border-b p-4 sm:p-5">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <button type="button" onClick={onBack} className="mb-2 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground lg:hidden">
              <ArrowLeft className="size-4" /> Emails
            </button>
            <h2 className="text-heading break-words">{e.subject || '(no subject)'}</h2>
            <div className="mt-1 text-sm text-muted-foreground">
              To <span className="text-foreground">{e.to}</span>
              {e.client ? <> · <Link className="text-link hover:underline" to={`/admin/clients/${e.client.id}`}>{e.client.fullName || 'client'}</Link></> : null}
              {' · '}{kindWords(e.kind)}
            </div>
          </div>
          <Status entry={st} size="lg" />
        </div>

        <dl className="grid grid-cols-1 gap-x-6 gap-y-1 text-caption text-muted-foreground sm:grid-cols-2">
          <div>Created {fmtDateTime(e.createdAt)}</div>
          <div>{e.sentAt ? `Sent ${fmtDateTime(e.sentAt)}` : e.lastAttemptAt ? `Last try ${fmtDateTime(e.lastAttemptAt)}` : 'Not sent'}{e.attemptCount > 1 ? ` · ${e.attemptCount} tries` : ''}</div>
          {e.deliveryAt ? <div>{statusOf('email', e.deliveryStatus).label} {fmtDateTime(e.deliveryAt)}</div> : e.status === 'Sent' ? <div>No delivery report yet</div> : null}
          {e.openedAt ? <div>Opened {fmtDateTime(e.openedAt)}</div> : null}
          {e.providerMessageId ? <div className="truncate" title="SendGrid message id">SendGrid {e.providerMessageId}</div> : null}
          {e.renewalRequestId ? <div><Link className="text-link hover:underline" to={`/admin/renewals/${e.renewalRequestId}`}>Open renewal</Link></div> : null}
          {e.leadId ? <div><Link className="text-link hover:underline" to={`/admin/leads/${e.leadId}`}>Open lead</Link></div> : null}
          {e.resendOfId ? <div><Link className="text-link hover:underline" to={`/admin/emails/${e.resendOfId}`}>Resend of an earlier email</Link></div> : null}
        </dl>

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
        </div>
      </div>

      <EmailBody detail={e} asText={asText} />
    </Card>
  )
}

/** The email as the customer saw it, in a locked-down frame (no scripts, no forms, links open a new tab). */
function EmailBody({ detail: e, asText }: { detail: EmailDetail; asText: boolean }) {
  if (!e.bodyHtml && !e.bodyText) {
    return <div className="p-8 text-center text-sm text-muted-foreground">The text of this email is no longer kept (we keep it for 180 days).</div>
  }
  if (asText || !e.bodyHtml) {
    return <pre className="max-h-[70dvh] overflow-y-auto whitespace-pre-wrap break-words p-4 text-sm sm:p-5">{e.bodyText}</pre>
  }
  const doc = `<base target="_blank">${e.bodyHtml}`
  return <iframe title="Email" sandbox="allow-popups allow-popups-to-escape-sandbox" srcDoc={doc} className="h-[70dvh] w-full bg-white" />
}
