import { type ReactNode } from 'react'

export function Pager({ total, skip, take, onPrev, onNext, label }: { total: number; skip: number; take: number; onPrev: () => void; onNext: () => void; label: string }) {
  return (
    <div className="flex items-center justify-between gap-2 mt-4 text-sm text-gray-500">
      <span>{total} {label}</span>
      <div className="flex gap-2">
        <button onClick={onPrev} disabled={skip === 0} className="rounded-md bg-white px-3 py-1.5 ring-1 ring-inset ring-gray-300 shadow-xs disabled:opacity-40 hover:bg-gray-50 text-gray-700 font-medium">Previous</button>
        <button onClick={onNext} disabled={skip + take >= total} className="rounded-md bg-white px-3 py-1.5 ring-1 ring-inset ring-gray-300 shadow-xs disabled:opacity-40 hover:bg-gray-50 text-gray-700 font-medium">Next</button>
      </div>
    </div>
  )
}

export function StatusPill({ status }: { status: string }) {
  const cls = (() => {
    switch (status) {
      case 'Completed':
      case 'OK':
      case 'RenewalCompleted':
        return 'bg-green-50 text-green-700 ring-green-600/20'
      case 'Failed':
      case 'RenewalFailed':
        return 'bg-red-50 text-red-700 ring-red-600/20'
      case 'Pending':
      case 'Processing':
      case 'Synced':
        return 'bg-blue-50 text-blue-700 ring-blue-600/20'
      case 'NotDueForRenewal':
      case 'WaitingForRenewalWindow':
        return 'bg-amber-50 text-amber-700 ring-amber-600/20'
      default:
        return 'bg-gray-50 text-gray-700 ring-gray-300'
    }
  })()
  return <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset ${cls}`}>{status}</span>
}

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <div className={`overflow-hidden rounded-lg bg-white shadow ${className}`}>{children}</div>
}

export function Table({ children }: { children: ReactNode }) {
  return (
    <div className="overflow-hidden rounded-lg bg-white shadow ring-1 ring-black/5">
      <div className="overflow-x-auto">
        <table className="min-w-full divide-y divide-gray-200 text-sm">{children}</table>
      </div>
    </div>
  )
}
