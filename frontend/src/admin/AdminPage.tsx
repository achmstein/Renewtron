import { type ReactNode } from 'react'

interface Props {
  title: string
  subtitle?: string
  caseId?: string          // e.g. "DOC-7421" — appears in metadata strip
  classification?: string  // e.g. "Renewals · Live"
  actions?: ReactNode
  children: ReactNode
}

const todayCode = () => {
  const d = new Date()
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}.${m}.${day}`
}

export default function AdminPage({ title, subtitle, caseId, classification = 'Internal', actions, children }: Props) {
  return (
    <main className="flex-1">
      <div className="mx-auto max-w-7xl px-4 pt-8 pb-12 sm:px-6 lg:px-8">
        {/* Dossier header */}
        <div className="bureau-dossier bureau-reveal">
          <div className="flex items-start justify-between gap-6">
            <div className="min-w-0">
              <div className="bureau-label" style={{ marginBottom: 6 }}>
                Section · {classification}
              </div>
              <h1 className="bureau-dossier-title">{title}</h1>
              {subtitle ? (
                <p className="mt-2 max-w-2xl text-[14px] leading-snug" style={{ color: 'var(--ink-soft)' }}>
                  {subtitle}
                </p>
              ) : null}
            </div>
            {actions ? <div className="flex shrink-0 items-center gap-2">{actions}</div> : null}
          </div>

          <div className="bureau-dossier-meta">
            <span>Case <strong>{caseId ?? `RGS-${todayCode().replace(/\./g, '')}`}</strong></span>
            <span>Filed <strong>{todayCode()}</strong></span>
            <span>Status <strong style={{ color: 'var(--verdict)' }}>● Active</strong></span>
            <span>Bureau <strong>BNR-AU</strong></span>
          </div>
        </div>

        {/* Content */}
        <div className="mt-8 bureau-reveal bureau-reveal-2">
          {children}
        </div>
      </div>
    </main>
  )
}
