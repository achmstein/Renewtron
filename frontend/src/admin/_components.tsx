import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Contact, TriangleAlert } from 'lucide-react'
import { api } from '../api/client'
import { Button } from '@/components/ui/button'
import { Dialog, DialogBody, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'

export function ErrorModal({ open, message, onClose, title = 'Error details' }: { open: boolean; message: string; onClose: () => void; title?: string }) {
  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-destructive/5 ring-1 ring-destructive/20">
              <TriangleAlert className="h-5 w-5 text-destructive" strokeWidth={1.5} />
            </div>
            <div className="min-w-0 text-left">
              <div className="text-xs font-medium text-destructive">Error</div>
              <DialogTitle>{title}</DialogTitle>
            </div>
          </div>
        </DialogHeader>
        <DialogBody className="max-h-[50dvh] overflow-y-auto">
          <p className="text-sm text-foreground break-words font-mono whitespace-pre-wrap">{message}</p>
        </DialogBody>
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
    <div className="mt-4 flex items-center justify-between border-t border-border pt-4">
      <div className="flex flex-1 justify-between sm:hidden">
        <button onClick={() => onPage(page - 1)} disabled={page === 1} className="relative inline-flex items-center rounded-md bg-card px-4 py-2 text-sm font-medium text-foreground ring-1 ring-inset ring-border hover:bg-muted/50 disabled:opacity-50">Previous</button>
        <button onClick={() => onPage(page + 1)} disabled={page * pageSize >= total} className="relative ml-3 inline-flex items-center rounded-md bg-card px-4 py-2 text-sm font-medium text-foreground ring-1 ring-inset ring-border hover:bg-muted/50 disabled:opacity-50">Next</button>
      </div>
      <div className="hidden sm:flex sm:flex-1 sm:items-center sm:justify-between">
        <div>
          <p className="text-xs font-mono text-muted-foreground tabular-nums">
            <span className="font-medium text-foreground">{from.toLocaleString()}</span>
            <span className="mx-1 text-muted-foreground/70">–</span>
            <span className="font-medium text-foreground">{to.toLocaleString()}</span>
            <span className="mx-1.5 text-muted-foreground/70">of</span>
            <span className="font-medium text-foreground">{total.toLocaleString()}</span>
          </p>
        </div>
        <div>
          <nav className="isolate inline-flex -space-x-px rounded-md shadow-sm">
            <button onClick={() => onPage(page - 1)} disabled={page === 1} className="relative inline-flex items-center rounded-l-md px-2 py-2 text-muted-foreground ring-1 ring-inset ring-border bg-card hover:bg-muted/50 disabled:opacity-50">
              <span className="sr-only">Previous</span>
              <svg className="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
                <path fillRule="evenodd" d="M12.79 5.23a.75.75 0 01-.02 1.06L8.832 10l3.938 3.71a.75.75 0 11-1.04 1.08l-4.5-4.25a.75.75 0 010-1.08l4.5-4.25a.75.75 0 011.06.02z" clipRule="evenodd" />
              </svg>
            </button>
            <span className="relative inline-flex items-center px-4 py-2 text-xs font-mono tabular-nums text-foreground ring-1 ring-inset ring-border bg-card">
              <span className="text-muted-foreground/70 mr-1.5">Page</span>{page}<span className="mx-1 text-muted-foreground/70">/</span>{pageCount}
            </span>
            <button onClick={() => onPage(page + 1)} disabled={page * pageSize >= total} className="relative inline-flex items-center rounded-r-md px-2 py-2 text-muted-foreground ring-1 ring-inset ring-border bg-card hover:bg-muted/50 disabled:opacity-50">
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

/**
 * "Client: Liza Lange →" for a lead, renewal, sale, key request or key letter. Renders nothing
 * until the record is on a client (the linker attaches new records every 15 minutes).
 */
export function ClientLink({ kind, id, className = '' }: {
  kind: 'lead' | 'renewal' | 'sale' | 'keyRequest' | 'keyLetter'
  id: string
  className?: string
}) {
  const { data } = useQuery({
    queryKey: ['admin-client-lookup', kind, id],
    queryFn: () => api.admin.clientLookup(kind, id),
    retry: false,
    staleTime: 60_000,
  })
  if (!data) return null
  return (
    <Link
      to={`/admin/clients/${data.id}`}
      className={`inline-flex items-center gap-1.5 rounded-md bg-primary/5 px-2 py-1 text-xs font-medium text-link ring-1 ring-primary/20 hover:bg-primary/10 ${className}`}
    >
      <Contact className="h-3.5 w-3.5" />
      Client: {data.fullName || 'unnamed'}
      {data.possibleDuplicate ? <span className="text-warning">· possible duplicate</span> : null}
      <span aria-hidden>→</span>
    </Link>
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
