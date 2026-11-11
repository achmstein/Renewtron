import { Link } from 'react-router-dom'
import { Cell } from '../_ui'
import { fmtDate, fmtMoney2, fmtTime } from '../_utils'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { BpayPill, PaymentPill, SourcePill, StatusCell, TimeInStatus } from './cells'
import { canRetry, type Renewal } from './model'

export function RenewalCard({ r, onError, onRetry, retrying }: { r: Renewal; onError: (m: string) => void; onRetry: (id: string) => void; retrying: boolean }) {
  return (
    <Card className="gap-4 p-4 sm:p-5">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="truncate font-medium">{r.businessName}</div>
          <div className="truncate text-caption text-muted-foreground">
            <span className="font-mono tabular-nums">{r.abn}</span>
            {r.lead ? <> · {r.lead.fullName}</> : null}
          </div>
        </div>
        <StatusCell r={r} onError={onError} />
      </div>
      <dl className="grid grid-cols-2 gap-x-4 gap-y-3">
        <Cell label="Amount" mono value={fmtMoney2(r.amount)} />
        <Cell label="Years" value={r.renewalYears} />
        <Cell label="Source" value={<div className="flex flex-wrap gap-1"><SourcePill source={r.source} /><BpayPill r={r} /></div>} />
        <Cell label="Payment" value={<PaymentPill r={r} />} />
        <Cell label="Time in status" value={<TimeInStatus r={r} />} />
        <Cell label="Started" value={`${fmtDate(r.initiatedAt)} · ${fmtTime(r.initiatedAt)}`} />
      </dl>
      <div className="flex items-center justify-end gap-1.5 border-t pt-4">
        {canRetry(r) ? (
          <Button size="sm" variant="outline" onClick={() => onRetry(r.id)} disabled={retrying}>
            {retrying ? 'Retrying…' : 'Retry'}
          </Button>
        ) : null}
        <Button asChild size="sm" variant="ghost"><Link to={`/admin/renewals/${r.id}`}>Open</Link></Button>
      </div>
    </Card>
  )
}
