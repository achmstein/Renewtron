import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Loader2 } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../../api/client'
import { Cell } from '../_ui'
import { fmtMoney0 } from '../_utils'
import { Button } from '@/components/ui/button'
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

/**
 * Runs the queue-vs-DB reconciliation. Opens straight into a dry-run preview —
 * nothing changes until the operator explicitly runs it live.
 */
export function ReconcileDialog({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: () => void }) {
  const [maxRequeue, setMaxRequeue] = useState(25)
  const { data: preview, isFetching } = useQuery({
    queryKey: ['reconcile-preview', maxRequeue],
    queryFn: () => api.admin.reconcileRenewals({ dryRun: true, maxRequeue }),
    enabled: open,
    staleTime: 0,
  })

  const liveMutation = useMutation({
    mutationFn: () => api.admin.reconcileRenewals({ dryRun: false, maxRequeue }),
  })
  const runLive = () => {
    void sileo.promise(liveMutation.mutateAsync(), {
      loading: { title: 'Reconciling…' },
      success: (r) => ({
        title: `Reconciled — ${r.requeued} re-queued`,
        description: [
          r.needsVerification > 0 ? `${r.needsVerification} flagged for payment verification` : null,
          r.markedStale > 0 ? `${r.markedStale} stale runs surfaced as failed` : null,
          r.requeueCapped > 0 ? `${r.requeueCapped} over the cap — run again to continue` : null,
        ].filter(Boolean).join(' · ') || 'Queue and database agree.',
      }),
      error: (e) => ({ title: 'Reconcile failed', description: e instanceof Error ? e.message : undefined }),
    }).then(() => { onDone(); onClose() }).catch(() => {})
  }

  const nothingToDo = preview && preview.scanned === 0

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o && !liveMutation.isPending) onClose() }}>
      <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Reconcile renewals</DialogTitle>
          <DialogDescription>
            Safe renewals are re-queued; any that may already hold an ASIC payment are flagged for you.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-3">
          {isFetching && !preview ? (
            <div className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground">
              <Loader2 className="size-4 animate-spin" /> Checking what would change…
            </div>
          ) : preview ? (
            <>
              <dl className="grid grid-cols-2 gap-x-4 gap-y-3 rounded-lg border p-3 sm:grid-cols-3">
                <Cell label="Checked" value={preview.scanned.toLocaleString()} />
                <Cell label="Re-queue" value={preview.requeued.toLocaleString()} />
                <Cell label="To verify" value={preview.needsVerification.toLocaleString()} />
                <Cell label="Stale" value={preview.markedStale.toLocaleString()} />
                <Cell label="Over the cap" value={preview.requeueCapped.toLocaleString()} />
                <Cell label="Job running" value={preview.skippedLiveJob.toLocaleString()} />
              </dl>
              {preview.items.length > 0 ? (
                <div className="max-h-40 divide-y overflow-y-auto rounded-lg border">
                  {preview.items.slice(0, 25).map((i) => (
                    <div key={i.renewalId} className="flex flex-col gap-0.5 px-3 py-1.5 text-caption sm:flex-row sm:items-center sm:justify-between sm:gap-2">
                      <span className="min-w-0 truncate">{i.businessName ?? i.abn ?? i.renewalId}</span>
                      <span className="text-muted-foreground tabular-nums sm:shrink-0">{fmtMoney0(i.amount)} · {i.ageHours}h · {i.action}</span>
                    </div>
                  ))}
                </div>
              ) : null}
              <div className="flex flex-wrap items-center gap-2" title="Keeps the ASIC queue manageable">
                <Label htmlFor="reconcile-cap" className="text-caption font-normal text-muted-foreground">Re-queue at most</Label>
                <Input
                  id="reconcile-cap"
                  type="number" min={0} max={500} value={maxRequeue}
                  onChange={(e) => setMaxRequeue(Math.max(0, Math.min(500, Number(e.target.value) || 0)))}
                  className="h-8 w-20 tabular-nums"
                />
                <span className="text-caption text-muted-foreground">per run</span>
              </div>
            </>
          ) : null}
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={liveMutation.isPending}>Close</Button>
          <Button onClick={runLive} disabled={liveMutation.isPending || !preview || nothingToDo}>
            {liveMutation.isPending ? 'Running…' : nothingToDo ? 'Nothing to reconcile' : 'Run live'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
