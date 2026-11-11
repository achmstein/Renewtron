import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  AlertTriangle, ArrowLeft, Check, ChevronsUpDown, Code2, History, Loader2, Monitor, MoreHorizontal, PenLine,
  RotateCcw, Send, Smartphone, Type, User,
} from 'lucide-react'
import { sileo } from 'sileo'
import { templatesApi, type Draft, type TemplateDetail, type TemplateProblem } from '../api/emailTemplates'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from '@/components/ui/dialog'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Switch } from '@/components/ui/switch'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { Skeletonizer } from '@/components/skeletonizer'
import { cn } from '@/lib/utils'
import { fmtDateTime } from './_utils'
import { fromEditor, toEditor } from './email-templates/liquid'
import { CodeEditor, setCompletionVariables } from './email-templates/monaco'
import { VariablePicker, VisualEditor } from './email-templates/VisualEditor'
import { VersionHistory } from './email-templates/VersionHistory'

/*
 * One email template (/admin/emails/templates/:key). Left: the subject, the heading and the message,
 * written in the visual editor or in code. Right: the email as it will arrive, re-rendered as you type,
 * with the sample values or a real customer's, on a desktop or phone width, or as plain text. Saving is
 * refused while the preview shows problems; every save is a version (History) you can go back to.
 */

type Mode = 'visual' | 'code'
type Width = 'desktop' | 'phone' | 'text'

export default function EmailTemplateEditor() {
  const { key = '' } = useParams()
  const detail = useQuery({ queryKey: ['email-template', key], queryFn: () => templatesApi.get(key) })

  if (detail.error) {
    return (
      <div className="p-8">
        <Alert variant="destructive"><AlertDescription>{(detail.error as Error).message}</AlertDescription></Alert>
      </div>
    )
  }
  if (!detail.data) return <EditorSkeleton />
  // Remount on a new version so the editor starts from what's saved.
  return <Editor key={`${key}:${detail.data.current.version}`} detail={detail.data} />
}

function Editor({ detail }: { detail: TemplateDetail }) {
  const qc = useQueryClient()
  const key = detail.key
  const saved = detail.current
  const [draft, setDraft] = useState<Draft>({ subject: saved.subject, heading: saved.heading, body: saved.body })
  // What the saved body looks like after a trip through the visual editor (so opening it isn't a change).
  const [savedBody, setSavedBody] = useState(saved.body)
  const firstEmit = useRef(true)

  const initial = useMemo(() => (detail.isLayout ? { advanced: 'The layout is edited in code.' } : toEditor(saved.body)), [detail.isLayout, saved.body])
  const [mode, setMode] = useState<Mode>('html' in initial ? 'visual' : 'code')
  const [visualHtml, setVisualHtml] = useState('html' in initial ? initial.html : '')
  const [visualKey, setVisualKey] = useState(0)
  const codeOnly = 'advanced' in initial && detail.isLayout

  const [sampleId, setSampleId] = useState<string | null>(null)
  const [sampleLabel, setSampleLabel] = useState('Sample values')
  const [width, setWidth] = useState<Width>('desktop')
  // ?history=3 (from an email in the log): open History on the version that email used.
  const [params, setParams] = useSearchParams()
  const historyParam = params.get('history')
  const [historyOpen, setHistoryOpen] = useState(historyParam !== null)
  const openHistory = (open: boolean) => {
    setHistoryOpen(open)
    if (!open && historyParam !== null) setParams((p) => { p.delete('history'); return p }, { replace: true })
  }
  const [confirm, setConfirm] = useState<null | 'reset' | 'off'>(null)

  useEffect(() => setCompletionVariables(detail.variables), [detail.variables])

  const dirty = draft.subject !== saved.subject || draft.heading !== saved.heading || draft.body !== savedBody

  // Warn before closing the tab with unsaved changes.
  useEffect(() => {
    if (!dirty) return
    const onUnload = (e: BeforeUnloadEvent) => { e.preventDefault() }
    window.addEventListener('beforeunload', onUnload)
    return () => window.removeEventListener('beforeunload', onUnload)
  }, [dirty])

  // Re-render the preview a moment after typing stops.
  const [debounced, setDebounced] = useState(draft)
  useEffect(() => {
    const t = window.setTimeout(() => setDebounced(draft), 350)
    return () => window.clearTimeout(t)
  }, [draft])
  const preview = useQuery({
    queryKey: ['email-template-preview', key, debounced, sampleId],
    queryFn: () => templatesApi.preview(key, debounced, sampleId),
    placeholderData: keepPreviousData,
  })
  const problems = preview.data?.problems ?? []
  // While the draft has a problem the server can't render it: keep showing the last email that did render.
  const [lastGood, setLastGood] = useState<{ html: string; text: string } | null>(null)
  useEffect(() => {
    if (preview.data?.html) setLastGood({ html: preview.data.html, text: preview.data.text })
  }, [preview.data])
  const shown = preview.data?.html ? preview.data : lastGood
  const checking = preview.isFetching || debounced !== draft

  const refresh = async () => {
    await qc.invalidateQueries({ queryKey: ['email-template', key] })
    await qc.invalidateQueries({ queryKey: ['email-templates'] })
    await qc.invalidateQueries({ queryKey: ['email-template-versions', key] })
  }

  const save = useMutation({
    mutationFn: (note: string) => templatesApi.save(key, draft, note),
    onSuccess: async (r) => { sileo.success({ title: r.message }); await refresh() },
    onError: (e: Error) => sileo.error({ title: 'Not saved', description: e.message }),
  })
  const reset = useMutation({
    mutationFn: () => templatesApi.reset(key),
    onSuccess: async (r) => { sileo.success({ title: r.message }); setConfirm(null); await refresh() },
    onError: (e: Error) => sileo.error({ title: e.message }),
  })
  const enable = useMutation({
    mutationFn: (on: boolean) => templatesApi.setEnabled(key, on),
    onSuccess: async (r) => { sileo.success({ title: r.message }); setConfirm(null); await refresh() },
    onError: (e: Error) => sileo.error({ title: e.message }),
  })
  const test = useMutation({
    mutationFn: (to: string) => templatesApi.test(key, draft, sampleId, to || undefined),
    onSuccess: (r) => sileo.success({ title: 'Test sent', description: r.message }),
    onError: (e: Error) => sileo.error({ title: 'Test not sent', description: e.message }),
  })

  const onVisualChange = (html: string) => {
    const body = fromEditor(html)
    if (firstEmit.current) {
      firstEmit.current = false
      setSavedBody(body)
    }
    setDraft((d) => (d.body === body ? d : { ...d, body }))
  }

  const switchMode = (next: Mode) => {
    if (next === mode) return
    if (next === 'visual') {
      const conv = toEditor(draft.body)
      if ('advanced' in conv) {
        sileo.error({ title: 'This needs code view', description: conv.advanced })
        return
      }
      setVisualHtml(conv.html)
      setVisualKey((k) => k + 1)
    }
    setMode(next)
  }

  const fieldProblems = (field: string) => problems.filter((p) => p.field === field)

  return (
    <div className="flex flex-col lg:h-svh">
      {/* ── Header ── */}
      <header className="flex flex-wrap items-center gap-x-3 gap-y-2 border-b bg-card px-4 py-2.5">
        <Button asChild variant="ghost" size="icon-sm" aria-label="All templates">
          <Link to="/admin/emails/templates"><ArrowLeft /></Link>
        </Button>
        <div className="min-w-40 flex-1">
          <h1 className="truncate text-heading">{detail.name}</h1>
          <div className="truncate text-caption text-muted-foreground">
            {saved.version === 0 ? 'Default wording' : `Version ${saved.version}`}
            {dirty && ' · unsaved changes'}
          </div>
        </div>
        {!detail.isLayout && (
          <label className="flex items-center gap-2 text-caption text-muted-foreground" title={detail.enabled ? 'Renewtron sends this email' : 'Renewtron skips this email'}>
            <Switch
              checked={detail.enabled}
              disabled={enable.isPending}
              onCheckedChange={(on) => (on ? enable.mutate(true) : setConfirm('off'))}
              aria-label="Send this email"
            />
            <span className="hidden sm:inline">{detail.enabled ? 'On' : 'Off'}</span>
          </label>
        )}
        <Button variant="outline" size="sm" onClick={() => setHistoryOpen(true)}><History /> <span className="hidden sm:inline">History</span></Button>
        <TestButton busy={test.isPending} onSend={(to) => test.mutate(to)} disabled={problems.length > 0} />
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="outline" size="icon-sm" aria-label="More"><MoreHorizontal /></Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem disabled={!dirty} onSelect={() => {
              setDraft({ subject: saved.subject, heading: saved.heading, body: saved.body })
              const conv = toEditor(saved.body)
              if ('html' in conv) { setVisualHtml(conv.html); setVisualKey((k) => k + 1); firstEmit.current = mode === 'visual' }
              else if (mode === 'visual') setMode('code')
            }}>
              <RotateCcw /> Discard changes
            </DropdownMenuItem>
            <DropdownMenuItem disabled={saved.version === 0} onSelect={() => setConfirm('reset')}><RotateCcw /> Reset to default wording</DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
        <SaveButton dirty={dirty} blocked={problems.length > 0 || checking} busy={save.isPending} onSave={(note) => save.mutate(note)} />
      </header>

      {!detail.enabled && !detail.isLayout && (
        <div className="flex items-center gap-2 border-b bg-warning/10 px-4 py-2 text-caption">
          <AlertTriangle className="size-4 text-warning" /> This email is switched off: Renewtron skips it instead of sending it.
        </div>
      )}

      <div className="grid min-h-0 flex-1 lg:grid-cols-2">
        {/* ── Editor ── */}
        <section className="flex min-h-0 min-w-0 flex-col border-b lg:border-r lg:border-b-0">
          {!detail.isLayout && (
            <div className="space-y-3 border-b p-4">
              <LiquidField
                id="subject" label="Subject" value={draft.subject} variables={detail.variables}
                problems={fieldProblems('subject')} onChange={(subject) => setDraft((d) => ({ ...d, subject }))}
              />
              <LiquidField
                id="heading" label="Heading" hint="The title in the coloured band. Leave empty for none."
                value={draft.heading} variables={detail.variables}
                problems={fieldProblems('heading')} onChange={(heading) => setDraft((d) => ({ ...d, heading }))}
              />
            </div>
          )}

          <div className="flex items-center gap-2 border-b px-3 py-1.5">
            <span className="text-caption font-medium text-muted-foreground">{detail.isLayout ? 'Layout' : 'Message'}</span>
            {!codeOnly && (
              <Tabs value={mode} onValueChange={(v) => switchMode(v as Mode)} className="ml-auto">
                <TabsList className="h-8">
                  <TabsTrigger value="visual" className="text-caption"><PenLine /> Visual</TabsTrigger>
                  <TabsTrigger value="code" className="text-caption"><Code2 /> Code</TabsTrigger>
                </TabsList>
              </Tabs>
            )}
          </div>
          {'advanced' in initial && !detail.isLayout && mode === 'code' && (
            <div className="border-b bg-muted/50 px-4 py-2 text-caption text-muted-foreground">{initial.advanced}</div>
          )}
          {fieldProblems(detail.isLayout ? 'layout' : 'body').length > 0 && (
            <ProblemList problems={fieldProblems(detail.isLayout ? 'layout' : 'body')} className="m-3 mb-0" />
          )}

          <div className="flex min-h-[60vh] flex-1 flex-col lg:min-h-0">
            {mode === 'visual' ? (
              <VisualEditor key={visualKey} html={visualHtml} variables={detail.variables} onChange={onVisualChange} />
            ) : (
              <div className="min-h-[60vh] flex-1 lg:min-h-0">
                <CodeEditor value={draft.body} onChange={(body) => setDraft((d) => ({ ...d, body }))} />
              </div>
            )}
          </div>
        </section>

        {/* ── Preview ── */}
        <section className="flex min-h-0 min-w-0 flex-col bg-muted/30">
          <div className="flex flex-wrap items-center gap-2 border-b bg-card px-3 py-1.5">
            <SamplePicker templateKey={key} source={detail.previewSource} label={sampleLabel}
              onPick={(id, label) => { setSampleId(id); setSampleLabel(label) }} />
            <Tabs value={width} onValueChange={(v) => setWidth(v as Width)} className="ml-auto">
              <TabsList className="h-8">
                <TabsTrigger value="desktop" aria-label="Desktop"><Monitor /></TabsTrigger>
                <TabsTrigger value="phone" aria-label="Phone"><Smartphone /></TabsTrigger>
                <TabsTrigger value="text" aria-label="Plain text"><Type /></TabsTrigger>
              </TabsList>
            </Tabs>
            {checking && <Loader2 className="size-4 animate-spin text-muted-foreground" aria-label="Updating" />}
          </div>
          <div className="border-b bg-card px-4 py-2 text-sm">
            <span className="text-muted-foreground">Subject: </span>
            <span className="font-medium">{preview.data?.subject || '—'}</span>
          </div>
          {problems.length > 0 && <ProblemList problems={problems} className="m-3 mb-0" title="Can't save yet" />}
          <div className="flex min-h-[70vh] flex-1 justify-center overflow-auto p-3 lg:min-h-0">
            {preview.error ? (
              <Alert variant="destructive" className="h-fit"><AlertDescription>{(preview.error as Error).message}</AlertDescription></Alert>
            ) : width === 'text' ? (
              <pre className="h-fit w-full max-w-[640px] rounded-lg border bg-card p-5 font-sans text-sm whitespace-pre-wrap">{shown?.text}</pre>
            ) : (
              <iframe
                title="Email preview"
                sandbox="allow-popups allow-popups-to-escape-sandbox"
                srcDoc={shown?.html ?? ''}
                className={cn('h-full min-h-[70vh] rounded-lg border bg-white shadow-xs transition-[width] lg:min-h-0', width === 'phone' ? 'w-[375px]' : 'w-full max-w-[720px]')}
              />
            )}
          </div>
        </section>
      </div>

      <VersionHistory
        open={historyOpen}
        onOpenChange={openHistory}
        initialVersion={historyParam === null ? null : Number(historyParam)}
        templateKey={key}
        current={saved.version}
        currentContent={draft}
        isLayout={detail.isLayout}
        onRestored={refresh}
      />

      <Dialog open={confirm !== null} onOpenChange={(o) => !o && setConfirm(null)}>
        <DialogContent>
          {confirm === 'reset' ? (
            <DialogHeader>
              <DialogTitle>Go back to the default wording?</DialogTitle>
              <DialogDescription>
                The wording Renewtron started with becomes the new version. Your current version stays in History, so you can bring it back.
              </DialogDescription>
            </DialogHeader>
          ) : (
            <DialogHeader>
              <DialogTitle>Switch off “{detail.name}”?</DialogTitle>
              <DialogDescription>
                Renewtron won't send this email to anyone until it's switched back on. {detail.description}
              </DialogDescription>
            </DialogHeader>
          )}
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirm(null)}>Cancel</Button>
            {confirm === 'reset' ? (
              <Button onClick={() => reset.mutate()} disabled={reset.isPending}>Reset</Button>
            ) : (
              <Button variant="destructive" onClick={() => enable.mutate(false)} disabled={enable.isPending}>Switch off</Button>
            )}
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}

/** A one-line Liquid field (subject, heading) with a variable picker that inserts at the cursor. */
function LiquidField({ id, label, hint, value, variables, problems, onChange }: {
  id: string
  label: string
  hint?: string
  value: string
  variables: TemplateDetail['variables']
  problems: TemplateProblem[]
  onChange: (v: string) => void
}) {
  const ref = useRef<HTMLInputElement>(null)
  const insert = (name: string) => {
    const el = ref.current
    const at = el?.selectionStart ?? value.length
    const end = el?.selectionEnd ?? at
    const token = `{{ ${name} }}`
    onChange(value.slice(0, at) + token + value.slice(end))
    requestAnimationFrame(() => { el?.focus(); el?.setSelectionRange(at + token.length, at + token.length) })
  }
  return (
    <div className="space-y-1.5">
      <div className="flex items-center justify-between gap-2">
        <Label htmlFor={id}>{label}</Label>
        <VariablePicker variables={variables.filter((v) => v.kind === 'text')} onPick={insert} label="Insert" />
      </div>
      <Input id={id} ref={ref} value={value} onChange={(e) => onChange(e.target.value)} aria-invalid={problems.length > 0} />
      {problems.map((p, i) => <div key={i} className="text-caption text-destructive">{p.message}</div>)}
      {hint && !problems.length && <div className="text-caption text-muted-foreground">{hint}</div>}
    </div>
  )
}

function ProblemList({ problems, title, className }: { problems: TemplateProblem[]; title?: string; className?: string }) {
  const where: Record<string, string> = { subject: 'Subject', heading: 'Heading', body: 'Message', layout: 'Layout' }
  return (
    <Alert variant="destructive" className={className}>
      <AlertTriangle />
      {title && <AlertTitle>{title}</AlertTitle>}
      <AlertDescription>
        <ul className="space-y-0.5">
          {problems.map((p, i) => <li key={i}><span className="font-medium">{where[p.field] ?? p.field}:</span> {p.message}</li>)}
        </ul>
      </AlertDescription>
    </Alert>
  )
}

function SaveButton({ dirty, blocked, busy, onSave }: { dirty: boolean; blocked: boolean; busy: boolean; onSave: (note: string) => void }) {
  const [open, setOpen] = useState(false)
  const [note, setNote] = useState('')
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button size="sm" disabled={!dirty || blocked || busy} title={blocked && dirty ? 'Fix the problems first' : undefined}>
          {busy ? <Loader2 className="animate-spin" /> : <Check />} Save
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80 space-y-3">
        <div className="space-y-1.5">
          <Label htmlFor="save-note">What changed? (optional)</Label>
          <Textarea id="save-note" rows={2} value={note} onChange={(e) => setNote(e.target.value)} placeholder="e.g. Friendlier opening line" />
          <div className="text-caption text-muted-foreground">Shown in History. Used from the next email sent.</div>
        </div>
        <div className="flex justify-end">
          <Button size="sm" onClick={() => { onSave(note); setOpen(false); setNote('') }}>Save version</Button>
        </div>
      </PopoverContent>
    </Popover>
  )
}

function TestButton({ busy, disabled, onSend }: { busy: boolean; disabled: boolean; onSend: (to: string) => void }) {
  const [open, setOpen] = useState(false)
  const [to, setTo] = useState('')
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" disabled={busy || disabled}>
          {busy ? <Loader2 className="animate-spin" /> : <Send />} <span className="hidden sm:inline">Send test</span>
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80">
        <form className="space-y-3" onSubmit={(e) => { e.preventDefault(); onSend(to.trim()); setOpen(false) }}>
          <div className="space-y-1.5">
            <Label htmlFor="test-to">Send this draft to</Label>
            <Input id="test-to" type="email" placeholder="Your email" value={to} onChange={(e) => setTo(e.target.value)} />
            <div className="text-caption text-muted-foreground">Uses the values shown in the preview. The subject starts with [Test].</div>
          </div>
          <div className="flex justify-end"><Button size="sm" type="submit">Send</Button></div>
        </form>
      </PopoverContent>
    </Popover>
  )
}

/** Preview with: the sample values, or a real renewal or lead (searchable). */
function SamplePicker({ templateKey, source, label, onPick }: {
  templateKey: string
  source: TemplateDetail['previewSource']
  label: string
  onPick: (id: string | null, label: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  useEffect(() => {
    const t = window.setTimeout(() => setQuery(search.trim()), 250)
    return () => window.clearTimeout(t)
  }, [search])
  const samples = useQuery({
    queryKey: ['email-template-samples', templateKey, query],
    queryFn: () => templatesApi.samples(templateKey, query),
    enabled: open,
    placeholderData: keepPreviousData,
  })
  const what = source === 'lead' ? 'lead' : source === 'registration' ? 'registration' : 'renewal'
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button variant="ghost" size="sm" className="max-w-64 justify-between">
          <User /> <span className="truncate">{label}</span> <ChevronsUpDown className="opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-80 p-0" align="start">
        <Command shouldFilter={false}>
          <CommandInput placeholder={`Find a ${what} by name or email`} value={search} onValueChange={setSearch} />
          <CommandList>
            <CommandGroup>
              <CommandItem onSelect={() => { onPick(null, 'Sample values'); setOpen(false) }}>Sample values</CommandItem>
            </CommandGroup>
            <CommandGroup heading={`Recent ${what}s`}>
              {samples.data?.map((s) => (
                <CommandItem key={s.id} value={s.id} onSelect={() => { onPick(s.id, s.title || s.detail || what); setOpen(false) }} className="flex-col items-start gap-0">
                  <span className="text-sm">{s.title || '—'}</span>
                  <span className="text-caption text-muted-foreground">{s.detail} · {fmtDateTime(s.at)}</span>
                </CommandItem>
              ))}
            </CommandGroup>
            {samples.data?.length === 0 && <CommandEmpty>Nothing matches.</CommandEmpty>}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}

/**
 * The editor while the template loads: its real header, fields, tabs and preview bar, drawn as a
 * skeleton. The message editor and the preview are one bone each.
 */
function EditorSkeleton() {
  const field = (label: string, value: string) => (
    <div className="space-y-1.5">
      <div className="flex items-center justify-between gap-2">
        <Label>{label}</Label>
        <Button variant="ghost" size="sm">Insert</Button>
      </div>
      <Input defaultValue={value} />
    </div>
  )
  return (
    <Skeletonizer loading className="lg:h-svh">
      <div className="flex flex-col lg:h-svh">
        <header className="flex flex-wrap items-center gap-x-3 gap-y-2 border-b bg-card px-4 py-2.5">
          <Button variant="ghost" size="icon-sm"><ArrowLeft /></Button>
          <div className="min-w-40 flex-1">
            <h1 className="truncate text-heading">Renewal confirmation</h1>
            <div className="truncate text-caption text-muted-foreground">Version 3</div>
          </div>
          <Switch checked />
          <Button variant="outline" size="sm"><History /> <span className="hidden sm:inline">History</span></Button>
          <Button variant="outline" size="sm"><Send /> Send test</Button>
          <Button variant="outline" size="icon-sm"><MoreHorizontal /></Button>
          <Button size="sm">Save</Button>
        </header>
        <div className="grid min-h-0 flex-1 lg:grid-cols-2">
          <section className="flex min-h-0 min-w-0 flex-col border-b lg:border-r lg:border-b-0">
            <div className="space-y-3 border-b p-4">
              {field('Subject', 'Your business name {{ business_name }} is renewed')}
              {field('Heading', 'All done')}
            </div>
            <div className="flex items-center gap-2 border-b px-3 py-1.5">
              <span className="text-caption font-medium text-muted-foreground">Message</span>
              <div className="ml-auto h-8 w-40" data-skeleton="bone" />
            </div>
            <div className="m-4 min-h-[50vh] flex-1 rounded-lg" data-skeleton="bone" />
          </section>
          <section className="flex min-h-0 min-w-0 flex-col bg-muted/30">
            <div className="flex flex-wrap items-center gap-2 border-b bg-card px-3 py-1.5">
              <Button variant="outline" size="sm">Sample values</Button>
              <div className="ml-auto h-8 w-28" data-skeleton="bone" />
            </div>
            <div className="border-b bg-card px-4 py-2 text-sm">
              <span className="text-muted-foreground">Subject: </span>
              <span className="font-medium">Your business name OKAFOR PLUMBING is renewed</span>
            </div>
            <div className="flex min-h-[70vh] flex-1 justify-center p-3 lg:min-h-0">
              <div className="h-full min-h-[70vh] w-full max-w-[720px] rounded-lg lg:min-h-0" data-skeleton="bone" />
            </div>
          </section>
        </div>
      </div>
    </Skeletonizer>
  )
}
