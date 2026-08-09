/**
 * Inbound link prefill.
 *
 * Ontraport (and any other email tool) can deep-link into the wizard with merge
 * fields on the query string, e.g.
 *
 *   https://businessnames.applyforanabn.com.au/?abn=[ABN]&name=[First Name] [Last Name]
 *     &email=[Email]&mobile=[Cell Phone]&cid=[Contact ID]
 *
 * Whoever builds the link picks the parameter names, so we accept a generous set
 * of aliases and normalise the values. Everything captured on the landing page is
 * stashed in sessionStorage so later wizard steps still have it after navigation.
 */

export interface Prefill {
  abn: string
  fullName: string
  email: string
  mobile: string
  dob: string
  contactId: string
  source: string
}

const EMPTY: Prefill = { abn: '', fullName: '', email: '', mobile: '', dob: '', contactId: '', source: '' }

const STORAGE_KEY = 'renewtron.prefill'

const ALIASES: Record<keyof Prefill, string[]> = {
  abn: ['abn', 'businessabn', 'business_abn', 'acn'],
  fullName: ['name', 'fullname', 'full_name', 'contactname', 'contact_name'],
  email: ['email', 'emailaddress', 'email_address', 'e'],
  mobile: ['mobile', 'mobilenumber', 'mobile_number', 'phone', 'cell', 'cellphone', 'cell_phone', 'sms'],
  dob: ['dob', 'dateofbirth', 'date_of_birth', 'birthdate'],
  contactId: ['cid', 'contactid', 'contact_id', 'oid', 'ontraportid', 'ontraport_id'],
  source: ['source', 'utm_source', 'src'],
}

/** Ontraport merge fields render as "[ABN]" when the contact has no value. */
function isUnresolvedMergeField(value: string) {
  return /^\[.*\]$/.test(value.trim()) || /^~.*~$/.test(value.trim())
}

/** Key matching ignores case, spaces, dashes and underscores: "Cell_Phone" === "cellphone". */
function canonical(key: string) {
  return key.trim().toLowerCase().replace(/[^a-z0-9]/g, '')
}

function pick(params: URLSearchParams, keys: string[]): string {
  const wanted = new Set(keys.map(canonical))
  for (const [rawKey, rawValue] of params.entries()) {
    if (!wanted.has(canonical(rawKey))) continue
    const value = (rawValue ?? '').trim()
    if (!value || isUnresolvedMergeField(value)) continue
    return value
  }
  return ''
}

export function normalizeAbn(value: string): string {
  const digits = (value ?? '').replace(/\D/g, '')
  return digits.length === 11 ? digits : ''
}

/** 0412 345 678 / +61 412 345 678 / 61412345678 → 0412345678 */
export function normalizeMobile(value: string): string {
  let digits = (value ?? '').replace(/[^\d+]/g, '')
  if (digits.startsWith('+61')) digits = `0${digits.slice(3)}`
  else if (digits.startsWith('61') && digits.length === 11) digits = `0${digits.slice(2)}`
  digits = digits.replace(/\D/g, '')
  if (digits.length === 9 && digits.startsWith('4')) digits = `0${digits}`
  return digits.length >= 9 ? digits : ''
}

/**
 * Accepts yyyy-mm-dd, dd/mm/yyyy, mm/dd/yyyy and unix seconds (Ontraport date
 * fields are stored as unix timestamps). Returns the yyyy-mm-dd a date input wants.
 */
export function normalizeDob(value: string): string {
  const raw = (value ?? '').trim()
  if (!raw) return ''

  if (/^\d{4}-\d{2}-\d{2}$/.test(raw)) return raw

  if (/^\d{9,11}$/.test(raw)) {
    const seconds = Number(raw)
    if (!Number.isFinite(seconds)) return ''
    return toIsoDate(new Date(seconds * 1000))
  }

  const slashed = raw.match(/^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$/)
  if (slashed) {
    const [, first, second, year] = slashed
    // Australian sites get dd/mm/yyyy; fall back to mm/dd/yyyy only when the
    // first component can't be a day.
    const dayFirst = Number(first) > 12 || Number(second) <= 12
    const day = dayFirst ? first : second
    const month = dayFirst ? second : first
    return `${year}-${month.padStart(2, '0')}-${day.padStart(2, '0')}`
  }

  const parsed = new Date(raw)
  return Number.isNaN(parsed.getTime()) ? '' : toIsoDate(parsed)
}

function toIsoDate(date: Date) {
  if (Number.isNaN(date.getTime())) return ''
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

function joinName(params: URLSearchParams) {
  const first = pick(params, ['firstname', 'first_name', 'fname', 'first'])
  const last = pick(params, ['lastname', 'last_name', 'lname', 'surname', 'last'])
  return [first, last].filter(Boolean).join(' ')
}

/** `businessnames.applyforanabn.com.au/?12345678901` — no key, just the ABN. */
function bareAbn(search: string) {
  const raw = search.replace(/^\?/, '').trim()
  return /^\d[\d\s]*$/.test(raw) ? normalizeAbn(raw) : ''
}

export function parsePrefill(search: string): Prefill {
  const params = new URLSearchParams(search)
  return {
    abn: normalizeAbn(pick(params, ALIASES.abn)) || bareAbn(search),
    fullName: pick(params, ALIASES.fullName) || joinName(params),
    email: pick(params, ALIASES.email).toLowerCase(),
    mobile: normalizeMobile(pick(params, ALIASES.mobile)),
    dob: normalizeDob(pick(params, ALIASES.dob)),
    contactId: pick(params, ALIASES.contactId),
    source: pick(params, ALIASES.source),
  }
}

function hasAnything(p: Prefill) {
  return Boolean(p.abn || p.fullName || p.email || p.mobile || p.dob || p.contactId || p.source)
}

function read(): Prefill {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    return raw ? { ...EMPTY, ...(JSON.parse(raw) as Partial<Prefill>) } : EMPTY
  } catch {
    return EMPTY
  }
}

function write(value: Prefill) {
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(value))
  } catch {
    /* private browsing — prefill just won't survive the next navigation */
  }
}

/**
 * Merges anything on the current query string into the stored prefill and returns
 * the result. Query string wins so a fresh link always overrides a stale session.
 */
export function capturePrefill(search: string): Prefill {
  const incoming = parsePrefill(search)
  if (!hasAnything(incoming)) return read()

  const merged = { ...read() }
  for (const key of Object.keys(EMPTY) as Array<keyof Prefill>) {
    if (incoming[key]) merged[key] = incoming[key]
  }
  write(merged)
  return merged
}

export function getPrefill(): Prefill {
  return read()
}

export function clearPrefill() {
  try {
    sessionStorage.removeItem(STORAGE_KEY)
  } catch {
    /* ignore */
  }
}
