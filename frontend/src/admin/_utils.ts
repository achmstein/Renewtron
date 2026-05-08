// Shared formatting + time helpers used across admin pages.

export function fmtMoney0(n: number): string {
  return '$' + Math.round(n).toLocaleString('en-AU')
}

export function fmtMoney2(n: number): string {
  return '$' + n.toLocaleString('en-AU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

export function fmtDate(s: string): string {
  return new Date(s).toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })
}

export function fmtTime(s: string): string {
  return new Date(s).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hour12: false })
}

export function fmtDateTime(s: string): string {
  return `${fmtDate(s)} · ${fmtTime(s)}`
}

/** "17m ago", "3h ago", "2d ago" — relative-to-now. */
export function relativeTime(iso: string): string {
  const t = new Date(iso).getTime()
  const diff = Date.now() - t
  const s = Math.floor(diff / 1000)
  if (s < 60) return `${s}s ago`
  const m = Math.floor(s / 60)
  if (m < 60) return `${m}m ago`
  const h = Math.floor(m / 60)
  if (h < 24) return `${h}h ago`
  const d = Math.floor(h / 24)
  return `${d}d ago`
}

/** "4h", "30m", "2d" — for time-in-status display. Returns just a duration, no "ago". */
export function durationShort(hours: number): string {
  if (!isFinite(hours) || hours < 0) return '—'
  if (hours < 1) return `${Math.round(hours * 60)}m`
  if (hours < 48) return `${Math.round(hours)}h`
  return `${Math.round(hours / 24)}d`
}
