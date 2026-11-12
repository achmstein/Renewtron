import { Link } from 'react-router-dom'
import { fmtMoney0 } from '../_utils'
import { Card } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { DueWords, OntraportStatusPill } from './cells'
import type { SaleRow } from './model'

export function UpNextCard({ s }: { s: SaleRow }) {
  // Up-next rows all have a due date (the server only picks those).
  const days = s._daysUntil ?? 0
  const body = (
    <>
      <div className="text-caption text-muted-foreground tabular-nums"><DueWords days={days} /></div>
      <div className="truncate font-medium" title={s.businessName}>{s.businessName}</div>
      <div className="truncate text-caption text-muted-foreground">{s.contactName || s.email}</div>
      <div className="mt-auto flex items-center justify-between gap-2 pt-2">
        <span className="font-mono text-sm tabular-nums">{fmtMoney0(s.amountPaid)}</span>
        <OntraportStatusPill status={s.status} />
      </div>
    </>
  )
  // When a renewal exists, the whole card opens it. Otherwise it's a plain tile.
  return (
    <Card className={cn('relative gap-0.5 p-4', s.renewalRequestId && 'transition-colors hover:bg-muted/50')}>
      {s.renewalRequestId ? (
        <Link to={s.orderId ? `/admin/renewal-orders/${s.orderId}` : `/admin/renewals/${s.renewalRequestId}`} className="absolute inset-0 rounded-xl" aria-label={`Open the renewal for ${s.businessName}`} />
      ) : null}
      {body}
    </Card>
  )
}
