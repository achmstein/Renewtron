import { type ReactNode, useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'

export function ErrorModal({ open, message, onClose, title = 'Error details' }: { open: boolean; message: string; onClose: () => void; title?: string }) {
  // Lock body scroll while open and close on Escape.
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    const prev = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.removeEventListener('keydown', onKey)
      document.body.style.overflow = prev
    }
  }, [open, onClose])

  if (!open) return null
  // Portal to document.body so any transformed ancestor (e.g. the .fade-in route
  // wrapper in AdminLayout) doesn't trap our `position: fixed` overlay.
  return createPortal(
    <div className="fixed inset-0 z-[100]" aria-labelledby="modal-title" role="dialog" aria-modal="true">
      <div className="absolute inset-0 bg-zinc-950/70 backdrop-blur-sm transition-opacity" onClick={onClose} />
      <div className="absolute inset-0 overflow-y-auto">
        <div className="flex min-h-full items-end justify-center p-4 sm:items-center sm:p-0">
          <div className="relative w-full overflow-hidden rounded-xl bg-white px-4 pb-4 pt-5 text-left shadow-xl sm:my-8 sm:max-w-lg sm:p-6">
            <div className="sm:flex sm:items-start">
              <div className="mx-auto flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-full bg-red-50 ring-1 ring-red-100 sm:mx-0 sm:h-10 sm:w-10">
                <svg className="h-5 w-5 text-red-600" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126zM12 15.75h.007v.008H12v-.008z" />
                </svg>
              </div>
              <div className="mt-3 text-center sm:ml-4 sm:mt-0 sm:text-left">
                <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-red-700">Error</div>
                <h3 className="mt-0.5 text-base font-semibold text-zinc-900 tracking-tight" id="modal-title">{title}</h3>
                <div className="mt-2"><p className="text-sm text-zinc-700 break-words font-mono whitespace-pre-wrap">{message}</p></div>
              </div>
            </div>
            <div className="mt-5 sm:mt-4 sm:flex sm:flex-row-reverse">
              <button onClick={onClose} type="button" className="inline-flex w-full justify-center rounded-md bg-zinc-900 px-3 py-2 text-sm font-medium text-white hover:bg-zinc-800 transition sm:ml-3 sm:w-auto">Close</button>
            </div>
          </div>
        </div>
      </div>
    </div>,
    document.body,
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

export function ViewToggle<T extends string>({ value, onChange, options }: { value: T; onChange: (v: T) => void; options: Array<{ value: T; icon: ReactNode; label: string }> }) {
  return (
    <div className="inline-flex rounded-md shadow-sm" role="group">
      {options.map((opt, i) => {
        const active = value === opt.value
        const cls = active ? 'bg-blue-600 text-white' : 'bg-white text-gray-700 hover:bg-gray-50'
        const radius = i === 0 ? 'rounded-l-md' : i === options.length - 1 ? 'rounded-r-md' : ''
        return (
          <button key={opt.value} type="button" onClick={() => onChange(opt.value)} title={opt.label} className={`${cls} ${radius} inline-flex items-center px-3 py-2 text-sm font-semibold ring-1 ring-inset ring-gray-300`}>
            {opt.icon}
          </button>
        )
      })}
    </div>
  )
}

export function IconTable() {
  return (
    <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
      <path strokeLinecap="round" strokeLinejoin="round" d="M3.375 19.5h17.25m-17.25 0a1.125 1.125 0 01-1.125-1.125M3.375 19.5h7.5c.621 0 1.125-.504 1.125-1.125m-9.75 0V5.625m0 12.75v-1.5c0-.621.504-1.125 1.125-1.125m18.375 2.625V5.625m0 12.75c0 .621-.504 1.125-1.125 1.125m1.125-1.125v-1.5c0-.621-.504-1.125-1.125-1.125m0 3.75h-7.5A1.125 1.125 0 0112 18.375m9.75-12.75c0-.621-.504-1.125-1.125-1.125H3.375c-.621 0-1.125.504-1.125 1.125m19.5 0v1.5c0 .621-.504 1.125-1.125 1.125M2.25 5.625v1.5c0 .621.504 1.125 1.125 1.125m0 0h17.25m-17.25 0h7.5c.621 0 1.125.504 1.125 1.125M3.375 8.25c-.621 0-1.125.504-1.125 1.125v1.5c0 .621.504 1.125 1.125 1.125m17.25-3.75h-7.5c-.621 0-1.125.504-1.125 1.125m8.625-1.125c.621 0 1.125.504 1.125 1.125v1.5c0 .621-.504 1.125-1.125 1.125m-17.25 0h7.5m-7.5 0c-.621 0-1.125.504-1.125 1.125v1.5c0 .621.504 1.125 1.125 1.125M12 10.875v-1.5m0 1.5c0 .621-.504 1.125-1.125 1.125M12 10.875c0 .621.504 1.125 1.125 1.125m-2.25 0c.621 0 1.125.504 1.125 1.125M13.125 12h7.5m-7.5 0c-.621 0-1.125.504-1.125 1.125M20.625 12c.621 0 1.125.504 1.125 1.125v1.5c0 .621-.504 1.125-1.125 1.125m-17.25 0h7.5M12 14.625v-1.5m0 1.5c0 .621-.504 1.125-1.125 1.125M12 14.625c0 .621.504 1.125 1.125 1.125m-2.25 0c.621 0 1.125.504 1.125 1.125m0 1.5v-1.5m0 0c0-.621.504-1.125 1.125-1.125m0 0h7.5" />
    </svg>
  )
}
export function IconCards() {
  return (
    <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
      <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6A2.25 2.25 0 016 3.75h2.25A2.25 2.25 0 0110.5 6v2.25a2.25 2.25 0 01-2.25 2.25H6a2.25 2.25 0 01-2.25-2.25V6zM3.75 15.75A2.25 2.25 0 016 13.5h2.25a2.25 2.25 0 012.25 2.25V18a2.25 2.25 0 01-2.25 2.25H6A2.25 2.25 0 013.75 18v-2.25zM13.5 6a2.25 2.25 0 012.25-2.25H18A2.25 2.25 0 0120.25 6v2.25A2.25 2.25 0 0118 10.5h-2.25a2.25 2.25 0 01-2.25-2.25V6zM13.5 15.75a2.25 2.25 0 012.25-2.25H18a2.25 2.25 0 012.25 2.25V18A2.25 2.25 0 0118 20.25h-2.25A2.25 2.25 0 0113.5 18v-2.25z" />
    </svg>
  )
}

export function IconFilter() {
  return (
    <svg className="h-4 w-4 mr-2" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
      <path strokeLinecap="round" strokeLinejoin="round" d="M12 3c2.755 0 5.455.232 8.083.678.533.09.917.556.917 1.096v1.044a2.25 2.25 0 01-.659 1.591l-5.432 5.432a2.25 2.25 0 00-.659 1.591v2.927a2.25 2.25 0 01-1.244 2.013L9.75 21v-6.568a2.25 2.25 0 00-.659-1.591L3.659 7.409A2.25 2.25 0 013 5.818V4.774c0-.54.384-1.006.917-1.096A48.32 48.32 0 0112 3z" />
    </svg>
  )
}

export function IconRefresh({ spinning }: { spinning?: boolean }) {
  if (spinning) {
    return (
      <svg className="animate-spin h-4 w-4" fill="none" viewBox="0 0 24 24">
        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
      </svg>
    )
  }
  return (
    <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
      <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />
    </svg>
  )
}

export function FilterChip({ label, onRemove }: { label: string; onRemove: () => void }) {
  return (
    <span className="inline-flex items-center gap-x-1.5 rounded-full bg-blue-100 px-3 py-1 text-xs font-medium text-blue-700">
      {label}
      <button onClick={onRemove} type="button" className="group relative -mr-1 h-3.5 w-3.5 rounded-sm hover:bg-blue-200">
        <svg className="h-3.5 w-3.5 stroke-blue-600" fill="none" viewBox="0 0 8 8" strokeWidth="1.5">
          <path strokeLinecap="round" d="M1 1l6 6m0-6L1 7" />
        </svg>
      </button>
    </span>
  )
}

export function FilterPopover({ open, onClose, title, children }: { open: boolean; onClose: () => void; title: string; children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null)
  if (!open) return null
  return (
    <>
      <div className="fixed inset-0 z-10" onClick={onClose}></div>
      <div ref={ref} className="absolute right-0 z-20 mt-2 w-80 origin-top-right rounded-xl bg-white shadow-lg ring-1 ring-zinc-200 focus:outline-none">
        <div className="p-4">
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500 mb-2">{title}</div>
          {children}
        </div>
      </div>
    </>
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
