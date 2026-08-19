import { useEffect, useState } from 'react'
import { TriangleAlert } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'

export function ErrorModal({ open, message, onClose, title = 'Error details' }: { open: boolean; message: string; onClose: () => void; title?: string }) {
  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-red-50 ring-1 ring-red-100">
              <TriangleAlert className="h-5 w-5 text-red-600" strokeWidth={1.5} />
            </div>
            <div className="min-w-0 text-left">
              <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-red-700">Error</div>
              <DialogTitle>{title}</DialogTitle>
            </div>
          </div>
        </DialogHeader>
        <div className="max-h-[50dvh] overflow-y-auto">
          <p className="text-sm text-zinc-700 break-words font-mono whitespace-pre-wrap">{message}</p>
        </div>
        <DialogFooter>
          <Button onClick={onClose} className="bg-zinc-900 hover:bg-zinc-800">Close</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function Pagination({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pageCount = Math.max(1, Math.ceil(total / pageSize))
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, total)
  return (
    <div className="mt-4 flex items-center justify-between border-t border-zinc-200 pt-4">
      <div className="flex flex-1 justify-between sm:hidden">
        <button onClick={() => onPage(page - 1)} disabled={page === 1} className="relative inline-flex items-center rounded-md bg-white px-4 py-2 text-sm font-medium text-zinc-700 ring-1 ring-inset ring-zinc-300 hover:bg-zinc-50 disabled:opacity-50">Previous</button>
        <button onClick={() => onPage(page + 1)} disabled={page * pageSize >= total} className="relative ml-3 inline-flex items-center rounded-md bg-white px-4 py-2 text-sm font-medium text-zinc-700 ring-1 ring-inset ring-zinc-300 hover:bg-zinc-50 disabled:opacity-50">Next</button>
      </div>
      <div className="hidden sm:flex sm:flex-1 sm:items-center sm:justify-between">
        <div>
          <p className="text-xs font-mono text-zinc-500 tabular-nums">
            <span className="font-medium text-zinc-900">{from.toLocaleString()}</span>
            <span className="mx-1 text-zinc-400">–</span>
            <span className="font-medium text-zinc-900">{to.toLocaleString()}</span>
            <span className="mx-1.5 text-zinc-400">of</span>
            <span className="font-medium text-zinc-900">{total.toLocaleString()}</span>
          </p>
        </div>
        <div>
          <nav className="isolate inline-flex -space-x-px rounded-md shadow-sm">
            <button onClick={() => onPage(page - 1)} disabled={page === 1} className="relative inline-flex items-center rounded-l-md px-2 py-2 text-zinc-500 ring-1 ring-inset ring-zinc-300 bg-white hover:bg-zinc-50 disabled:opacity-50">
              <span className="sr-only">Previous</span>
              <svg className="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
                <path fillRule="evenodd" d="M12.79 5.23a.75.75 0 01-.02 1.06L8.832 10l3.938 3.71a.75.75 0 11-1.04 1.08l-4.5-4.25a.75.75 0 010-1.08l4.5-4.25a.75.75 0 011.06.02z" clipRule="evenodd" />
              </svg>
            </button>
            <span className="relative inline-flex items-center px-4 py-2 text-xs font-mono tabular-nums text-zinc-700 ring-1 ring-inset ring-zinc-300 bg-white">
              <span className="text-zinc-400 tracking-[0.14em] uppercase mr-1.5">Page</span>{page}<span className="mx-1 text-zinc-400">/</span>{pageCount}
            </span>
            <button onClick={() => onPage(page + 1)} disabled={page * pageSize >= total} className="relative inline-flex items-center rounded-r-md px-2 py-2 text-zinc-500 ring-1 ring-inset ring-zinc-300 bg-white hover:bg-zinc-50 disabled:opacity-50">
              <span className="sr-only">Next</span>
              <svg className="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
                <path fillRule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clipRule="evenodd" />
              </svg>
            </button>
          </nav>
        </div>
      </div>
    </div>
  )
}

export function useDebouncedValue<T>(value: T, delay = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const t = window.setTimeout(() => setV(value), delay)
    return () => window.clearTimeout(t)
  }, [value, delay])
  return v
}
