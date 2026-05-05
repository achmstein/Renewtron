import { type ReactNode, useEffect, useRef, useState } from 'react'

export function ErrorModal({ open, message, onClose, title = 'Error Details' }: { open: boolean; message: string; onClose: () => void; title?: string }) {
  if (!open) return null
  return (
    <div className="relative z-50" aria-labelledby="modal-title" role="dialog" aria-modal="true">
      <div className="fixed inset-0 bg-[var(--ink)]/70 backdrop-blur-[1px] transition-opacity" onClick={onClose}></div>
      <div className="fixed inset-0 z-10 overflow-y-auto">
        <div className="flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0">
          <div className="bureau-dossier relative w-full max-w-lg text-left sm:my-8">
            <div className="bureau-label" style={{ marginBottom: 6 }}>Incident Report</div>
            <h3 className="bureau-dossier-title" id="modal-title" style={{ fontSize: 22, lineHeight: 1.15 }}>
              {title}
            </h3>
            <div
              className="bureau-mono mt-4 max-h-[60vh] overflow-y-auto whitespace-pre-wrap break-words p-3 text-[12px] leading-relaxed"
              style={{
                background: 'var(--paper-deep)',
                border: '1px solid var(--hairline-deep)',
                color: 'var(--ink)',
              }}
            >
              {message}
            </div>
            <div className="mt-5 flex justify-end gap-2">
              <button onClick={onClose} type="button" className="bureau-btn bureau-btn-primary">
                Acknowledge
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

export function Pagination({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pageCount = Math.max(1, Math.ceil(total / pageSize))
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, total)
  return (
    <div className="mt-4 flex items-center justify-between border-t border-[var(--hairline-deep)] pt-4">
      <p className="bureau-meta">
        {total === 0
          ? '0 records'
          : <>Records <strong style={{ color: 'var(--ink)' }}>{from}–{to}</strong> of <strong style={{ color: 'var(--ink)' }}>{total}</strong></>}
      </p>
      <div className="flex items-center gap-2">
        <button
          onClick={() => onPage(page - 1)}
          disabled={page === 1}
          className="bureau-btn"
          aria-label="Previous page"
        >
          ←
        </button>
        <span className="bureau-mono px-3 py-1.5 text-[11px]" style={{ color: 'var(--ink)', letterSpacing: '0.08em' }}>
          PG {String(page).padStart(2, '0')} / {String(pageCount).padStart(2, '0')}
        </span>
        <button
          onClick={() => onPage(page + 1)}
          disabled={page * pageSize >= total}
          className="bureau-btn"
          aria-label="Next page"
        >
          →
        </button>
      </div>
    </div>
  )
}

export function ViewToggle<T extends string>({ value, onChange, options }: { value: T; onChange: (v: T) => void; options: Array<{ value: T; icon: ReactNode; label: string }> }) {
  return (
    <div className="inline-flex" role="group">
      {options.map((opt, i) => {
        const active = value === opt.value
        return (
          <button
            key={opt.value}
            type="button"
            onClick={() => onChange(opt.value)}
            title={opt.label}
            className={`bureau-btn ${active ? 'bureau-btn-active' : ''}`}
            style={{
              marginLeft: i === 0 ? 0 : -1,
              borderTopRightRadius: 0,
              borderTopLeftRadius: i === 0 ? 0 : 0,
              borderBottomRightRadius: 0,
              borderBottomLeftRadius: i === 0 ? 0 : 0,
            }}
          >
            {opt.icon}
          </button>
        )
      })}
    </div>
  )
}

export function IconTable() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
      <rect x="3" y="4" width="18" height="16" rx="0" />
      <path d="M3 10h18M3 14h18M9 4v16" />
    </svg>
  )
}

export function IconCards() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
      <rect x="3" y="3" width="8" height="8" />
      <rect x="13" y="3" width="8" height="8" />
      <rect x="3" y="13" width="8" height="8" />
      <rect x="13" y="13" width="8" height="8" />
    </svg>
  )
}

export function IconFilter() {
  return (
    <svg className="mr-1 h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path strokeLinecap="round" strokeLinejoin="round" d="M3 5h18M6 12h12M10 19h4" />
    </svg>
  )
}

export function IconRefresh({ spinning }: { spinning?: boolean }) {
  return (
    <svg
      className={`h-3.5 w-3.5 ${spinning ? 'bureau-spin' : ''}`}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
    >
      <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992V4.356M2.985 14.652v4.992h4.992M2.985 19.644l3.181-3.182a8.25 8.25 0 0013.803-3.7M21.015 9.348l-3.181 3.182a8.25 8.25 0 00-13.803 3.7" />
    </svg>
  )
}

export function FilterChip({ label, onRemove }: { label: string; onRemove: () => void }) {
  return (
    <span className="bureau-chip">
      {label}
      <button
        onClick={onRemove}
        type="button"
        aria-label="Remove filter"
        className="-mr-1 inline-flex h-3.5 w-3.5 items-center justify-center transition-colors"
        style={{ color: 'var(--stamp)' }}
      >
        <svg className="h-2.5 w-2.5" viewBox="0 0 8 8" fill="none" stroke="currentColor" strokeWidth="2">
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
      <div
        ref={ref}
        className="absolute right-0 z-20 mt-2 w-80 origin-top-right bureau-card bureau-reveal"
        style={{ boxShadow: '0 6px 0 var(--paper-deep), 0 6px 1px var(--hairline-deep)' }}
      >
        <div className="border-b border-[var(--hairline)] px-4 py-2.5" style={{ background: 'var(--paper-deep)' }}>
          <div className="bureau-label">Filter · {title}</div>
        </div>
        <div className="p-4">{children}</div>
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
