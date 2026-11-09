import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { History, Loader2, RotateCcw } from 'lucide-react'
import { sileo } from 'sileo'
import { templatesApi, type Draft, type TemplateContent, type TemplateVersion } from '@/api/emailTemplates'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { cn } from '@/lib/utils'
import { fmtDateTime, relativeTime } from '../_utils'
import { CodeDiff } from './monaco'

/*
 * History: every saved version of a template, newest first, ending with the wording Renewtron started
 * with. Pick one to compare it with what's in the editor now (or with the version before it), see how
 * many emails went out with it, and restore it (which saves it again as a new version).
 */

type Compare = 'draft' | 'previous'

const asText = (c: Pick<TemplateContent, 'subject' | 'heading' | 'body'>, isLayout: boolean) =>
  isLayout ? c.body : `Subject: ${c.subject}\nHeading: ${c.heading}\n\n${c.body}`

const SOURCE: Record<TemplateVersion['source'], string> = { edit: 'Edited', default: 'Reset to default', rollback: 'Restored' }

export function VersionHistory({ open, onOpenChange, templateKey, current, currentContent, isLayout, initialVersion, onRestored }: {
  open: boolean
  onOpenChange: (open: boolean) => void
  templateKey: string
  current: number
  currentContent: Draft
  isLayout: boolean
  /** The version to show first (e.g. the one an email used). */
  initialVersion?: number | null
  onRestored: () => Promise<void>
}) {
  const [picked, setPicked] = useState<number | null>(initialVersion ?? null)
  const [compare, setCompare] = useState<Compare>('draft')

  const list = useQuery({
    queryKey: ['email-template-versions', templateKey],
    queryFn: () => templatesApi.versions(templateKey),
    enabled: open,
  })
  const versions = list.data?.versions ?? []
  const selected = picked ?? (versions.length > 1 ? versions[1].version : 0)
  const previousOf = (v: number) => versions.find((x) => x.version < v)?.version ?? (v > 0 ? 0 : null)
  const other = compare === 'previous' ? previousOf(selected) : null

  const left = useQuery({
    queryKey: ['email-template-version', templateKey, selected],
    queryFn: () => templatesApi.version(templateKey, selected),
    enabled: open,
  })
  const right = useQuery({
    queryKey: ['email-template-version', templateKey, other],
    queryFn: () => templatesApi.version(templateKey, other!),
    enabled: open && other !== null,
  })

  const restore = useMutation({
    mutationFn: (v: number) => templatesApi.restore(templateKey, v),
    onSuccess: async (r) => { sileo.success({ title: r.message }); setPicked(null); await onRestored(); onOpenChange(false) },
    onError: (e: Error) => sileo.error({ title: 'Not restored', description: e.message }),
  })

  // The diff reads left → right as "then → now" for the draft, "before → this" for previous.
  const [original, modified] = compare === 'draft'
    ? [left.data ? asText(left.data, isLayout) : '', asText(currentContent, isLayout)]
    : [right.data ? asText(right.data, isLayout) : '', left.data ? asText(left.data, isLayout) : '']

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full gap-0 p-0 sm:max-w-5xl">
        <SheetHeader className="border-b">
          <SheetTitle className="flex items-center gap-2"><History className="size-4" /> History</SheetTitle>
          <SheetDescription>Every saved version. Restoring one saves it again as the newest version; nothing is lost.</SheetDescription>
        </SheetHeader>
        <div className="grid min-h-0 flex-1 md:grid-cols-[18rem_1fr]">
          <ol className="max-h-64 overflow-y-auto border-b md:max-h-none md:border-r md:border-b-0">
            {list.isLoading && <li className="p-4"><Skeleton className="h-24" /></li>}
            {versions.map((v) => (
              <VersionRow key={v.version} v={v} live={v.version === current} active={v.version === selected} onClick={() => setPicked(v.version)} />
            ))}
            {list.data && (
              <VersionRow
                v={{ version: 0, source: 'default', restoredFrom: null, note: null, createdAt: '', createdBy: null, sent: list.data.sentWithDefault }}
                live={current === 0}
                active={selected === 0}
                onClick={() => setPicked(0)}
              />
            )}
          </ol>
          <div className="flex min-h-[60vh] min-w-0 flex-col">
            <div className="flex flex-wrap items-center gap-2 border-b px-4 py-2">
              <span className="text-sm font-medium">{selected === 0 ? 'Default wording' : `Version ${selected}`}</span>
              <ToggleGroup type="single" variant="outline" size="sm" value={compare} onValueChange={(v) => v && setCompare(v as Compare)}>
                <ToggleGroupItem value="draft" className="text-caption">vs. the editor now</ToggleGroupItem>
                <ToggleGroupItem value="previous" className="text-caption" disabled={previousOf(selected) === null}>vs. the version before</ToggleGroupItem>
              </ToggleGroup>
              <Button
                size="sm"
                className="ml-auto"
                disabled={selected === current || restore.isPending}
                onClick={() => restore.mutate(selected)}
                title={selected === current ? 'This is the version in use' : undefined}
              >
                {restore.isPending ? <Loader2 className="animate-spin" /> : <RotateCcw />} Restore this version
              </Button>
            </div>
            <div className="flex justify-between border-b bg-muted/40 px-4 py-1 text-caption text-muted-foreground">
              <span>{compare === 'draft' ? (selected === 0 ? 'Default wording' : `Version ${selected}`) : other === 0 ? 'Default wording' : `Version ${other}`}</span>
              <span>{compare === 'draft' ? 'The editor now' : selected === 0 ? 'Default wording' : `Version ${selected}`}</span>
            </div>
            {left.error ? (
              <Alert variant="destructive" className="m-4"><AlertDescription>{(left.error as Error).message}</AlertDescription></Alert>
            ) : (
              <div className="min-h-[50vh] flex-1">
                <CodeDiff original={original} modified={modified} />
              </div>
            )}
          </div>
        </div>
      </SheetContent>
    </Sheet>
  )
}

function VersionRow({ v, live, active, onClick }: { v: TemplateVersion; live: boolean; active: boolean; onClick: () => void }) {
  return (
    <li>
      <button
        type="button"
        onClick={onClick}
        className={cn('w-full border-b px-4 py-3 text-left transition-colors hover:bg-muted/50', active && 'bg-muted')}
      >
        <div className="flex items-center gap-2">
          <span className="text-sm font-medium">{v.version === 0 ? 'Default wording' : `Version ${v.version}`}</span>
          {live && <Badge variant="secondary">In use</Badge>}
        </div>
        <div className="text-caption text-muted-foreground">
          {v.version === 0
            ? 'The wording Renewtron started with'
            : <>{v.source === 'rollback' && v.restoredFrom !== null ? `Restored v${v.restoredFrom}` : SOURCE[v.source]} · <span title={fmtDateTime(v.createdAt)}>{relativeTime(v.createdAt)}</span>{v.createdBy ? ` · ${v.createdBy.split('@')[0]}` : ''}</>}
        </div>
        {v.note && v.source === 'edit' && <div className="mt-0.5 line-clamp-2 text-caption">{v.note}</div>}
        <div className="text-caption text-muted-foreground">{v.sent === 0 ? 'Not sent yet' : `${v.sent.toLocaleString()} email${v.sent === 1 ? '' : 's'} sent`}</div>
      </button>
    </li>
  )
}
