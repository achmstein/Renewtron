import { Cell } from '../_ui'
import { fmtMoney0 } from '../_utils'
import { Button } from '@/components/ui/button'
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { SOURCE_WORDS, type Stats } from './model'

export function BulkRetryModal({ stats, dateFrom, dateTo, source, busy, onClose, onConfirm }: {
  stats: Stats
  dateFrom: string
  dateTo: string
  source: string
  busy: boolean
  onClose: () => void
  onConfirm: () => void
}) {
  return (
    <Dialog open onOpenChange={(o) => { if (!o && !busy) onClose() }}>
      <DialogContent className="max-h-[85dvh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Retry all failed renewals?</DialogTitle>
          <DialogDescription>
            Every failed renewal in the current filter goes back in the queue. Unpaid ones, and any ASIC may already
            have taken payment for, are skipped.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-3">
          <dl className="grid grid-cols-2 gap-x-4 gap-y-3 rounded-lg border p-3">
            <Cell label="Failed, last 30 days" value={fmtMoney0(stats.failedValue30d)} />
            {source ? <Cell label="Source" value={SOURCE_WORDS[source] ?? source} /> : null}
            {dateFrom ? <Cell label="From" value={dateFrom} /> : null}
            {dateTo ? <Cell label="To" value={dateTo} /> : null}
          </dl>
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={busy}>Cancel</Button>
          <Button onClick={onConfirm} disabled={busy}>
            {busy ? 'Retrying…' : 'Retry all'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
