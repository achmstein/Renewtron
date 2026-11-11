import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Loader2 } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../../api/client'
import { useDebouncedValue } from '../_components'
import { fmtMoney0 } from '../_utils'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

/**
 * Puts failed sales back into the daily processor's pool in controlled batches.
 * Opens straight into a dry-run preview — nothing changes until "Requeue" runs.
 */
export function RequeueFailedDialog({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: () => void }) {
  const [max, setMax] = useState(50)
  const [includePastDue, setIncludePastDue] = useState(false)
  const [errorInput, setErrorInput] = useState('')
  const errorContains = useDebouncedValue(errorInput, 300)

  const { data: preview, isFetching } = useQuery({
    queryKey: ['ontraport-requeue-preview', { max, includePastDue, errorContains }],
    queryFn: () => api.admin.requeueFailedOntraport({ dryRun: true, max, includePastDue, errorContains: errorContains || undefined }),
    enabled: open,
    staleTime: 0,
  })

  const liveMutation = useMutation({
    mutationFn: () => api.admin.requeueFailedOntraport({ dryRun: false, max, includePastDue, errorContains: errorContains || undefined }),
  })
  const runLive = () => {
    void sileo.promise(liveMutation.mutateAsync(), {
      loading: { title: 'Re-queuing failed sales…' },
      success: (r) => ({
        title: `Re-queued ${r.requeued} of ${r.totalMatching} failed sales`,
        description: 'They will process on the next 07:00 run, or immediately via Process eligible.',
      }),
      error: (e) => ({ title: 'Requeue failed', description: e instanceof Error ? e.message : undefined }),
    }).then(() => { onDone(); onClose() }).catch(() => {})
  }

  const nothingToDo = preview && preview.batchSize === 0

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o && !liveMutation.isPending) onClose() }}>
      <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Requeue failed sales</DialogTitle>
          <DialogDescription>Start with a small batch, watch how it goes, then run again.</DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-3">
          <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
            <div className="flex items-center gap-2">
              <Label htmlFor="requeue-max" className="text-caption font-normal text-muted-foreground">Batch size</Label>
              <Input
                id="requeue-max" type="number" min={1} max={500} value={max}
                onChange={(e) => setMax(Math.max(1, Math.min(500, Number(e.target.value) || 1)))}
                className="h-8 w-20 tabular-nums"
              />
            </div>
            <Label className="text-caption font-normal text-muted-foreground" title="Sales past ASIC’s renewal window are left out unless you tick this">
              <Checkbox checked={includePastDue} onCheckedChange={(v) => setIncludePastDue(v === true)} />
              Include past due
            </Label>
          </div>
          <Input
            value={errorInput}
            onChange={(e) => setErrorInput(e.target.value)}
            placeholder="Only errors containing… (optional)"
            className="h-8"
          />

          {isFetching && !preview ? (
            <div className="flex items-center justify-center gap-2 py-4 text-sm text-muted-foreground">
              <Loader2 className="size-4 animate-spin" /> Checking what would change…
            </div>
          ) : preview ? (
            <>
              <p className="text-sm">
                <span className="font-medium tabular-nums">{preview.batchSize}</span> of{' '}
                <span className="tabular-nums">{preview.totalMatching}</span> matching failed sales in this batch
              </p>
              {preview.items.length > 0 ? (
                <div className="max-h-36 divide-y overflow-y-auto rounded-lg border">
                  {preview.items.slice(0, 25).map((i) => (
                    <div key={i.id} className="flex items-center justify-between gap-2 px-3 py-1.5 text-caption">
                      <span className="min-w-0 truncate">{i.businessName} · <span className="font-mono tabular-nums text-muted-foreground">{i.abn}</span></span>
                      <span className="shrink-0 text-muted-foreground tabular-nums">{fmtMoney0(i.amountPaid)}</span>
                    </div>
                  ))}
                </div>
              ) : null}
            </>
          ) : null}
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={liveMutation.isPending}>Close</Button>
          <Button onClick={runLive} disabled={liveMutation.isPending || !preview || nothingToDo}>
            {liveMutation.isPending ? 'Re-queuing…' : nothingToDo ? 'Nothing matches' : `Requeue ${preview?.batchSize ?? ''}`}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
