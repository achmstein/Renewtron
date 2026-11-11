import { TriangleAlert } from 'lucide-react'
import { renewalStatus, Status } from '../_status'
import { durationShort, fmtDate, fmtTime } from '../_utils'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { SOURCE_WORDS, type Renewal } from './model'

export function StatusCell({ r, onError }: { r: Renewal; onError: (m: string) => void }) {
  return (
    <div className="flex shrink-0 items-center gap-1">
      <Status entry={renewalStatus(r)} />
      {r.status === 'Failed' && r.errorMessage ? (
        <Button variant="ghost" size="icon-sm" className="sm:size-6" onClick={() => onError(r.errorMessage ?? '')} title="See the error" aria-label="See the error">
          <TriangleAlert className="text-destructive" />
        </Button>
      ) : null}
    </div>
  )
}

export function SourcePill({ source }: { source: string }) {
  return <Badge variant="outline">{SOURCE_WORDS[source] ?? source}</Badge>
}

/** ASIC's BPAY bill for the renewal, when there is one. */
export function BpayPill({ r }: { r: Renewal }) {
  if (!r.bpayReference) return null
  return r.bpayPaidAt
    ? <Status entry={{ label: 'BPAY paid', meaning: 'done' }} />
    : <Status entry={{ label: 'BPAY to pay', meaning: 'action', hint: 'ASIC holds the renewal until it’s paid.' }} />
}

export function PaymentPill({ r }: { r: Renewal }) {
  if (r.paymentType === 'External') return <span className="text-caption text-muted-foreground">Paid elsewhere</span>
  if (!r.stripePaymentSucceeded) return <span className="text-caption text-muted-foreground">Card not charged</span>
  const brand = r.cardBrand ? r.cardBrand.charAt(0).toUpperCase() + r.cardBrand.slice(1) : 'Card'
  return <span className="text-caption text-muted-foreground">{brand}{r.cardLast4 ? <> ending <span className="font-mono tabular-nums">{r.cardLast4}</span></> : null}</span>
}

export function TimeInStatus({ r }: { r: Renewal }) {
  const isInflight = r.status === 'Pending' || r.status === 'Processing'
  const slow = isInflight && r.timeInStatusHours > 2
  const facts = [
    !isInflight && r.completedAt ? `closed ${fmtDate(r.completedAt)}` : null,
    r.status === 'Failed' && r.nextRetryAt ? `retries ${fmtDate(r.nextRetryAt)} ${fmtTime(r.nextRetryAt)}` : null,
    r.attemptCount > 1 ? `attempt ${r.attemptCount}` : null,
  ].filter(Boolean)
  return (
    <div>
      <div className={cn('flex items-center gap-1.5 tabular-nums', slow && 'font-medium')} title={slow ? 'In flight for over 2 hours' : undefined}>
        {slow ? <span aria-hidden className="size-2 rounded-full bg-warning" /> : null}
        {durationShort(r.timeInStatusHours)}{isInflight ? ' in flight' : ''}
      </div>
      {facts.length ? <div className="text-caption text-muted-foreground tabular-nums">{facts.join(' · ')}</div> : null}
    </div>
  )
}
