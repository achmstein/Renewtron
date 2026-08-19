import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { CircleCheck, Loader2, Search } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../api/client'
import { KickerLabel, PageHeader } from './_ui'

type SearchResult = { id: string; businessName: string; accountNumber: string; registrationDate: string }

const inputCls = 'mt-1 block w-full rounded-md border-zinc-300 shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 sm:text-sm px-3 py-2 border'
const labelCls = 'block text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500'

export default function ManualRenewal() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [pricing, setPricing] = useState<{ oneYearFee: number; threeYearFee: number } | null>(null)

  const [searchAbn, setSearchAbn] = useState('')
  const [searchResults, setSearchResults] = useState<SearchResult[]>([])
  const [selectedResult, setSelectedResult] = useState<SearchResult | null>(null)

  const [renewalYears, setRenewalYears] = useState<1 | 3>(1)
  const [customerEmail, setCustomerEmail] = useState('')
  const [mobileNumber, setMobileNumber] = useState('')
  const [dateOfBirth, setDateOfBirth] = useState('')
  const [amount, setAmount] = useState(0)

  useEffect(() => {
    api.pricing().then((p) => {
      setPricing(p)
      setAmount(p.oneYearFee)
    }).catch(() => {})
  }, [])

  // Try to prefill customer info from existing leads when ABN matches
  useEffect(() => {
    if (!selectedResult || customerEmail) return
    api.admin.leads({ search: searchAbn, take: 1 })
      .then((r) => {
        const found = r.items[0]
        if (found && !customerEmail) {
          setCustomerEmail(found.email ?? '')
          setMobileNumber(found.mobileNumber ?? '')
        }
      })
      .catch(() => {})
  }, [selectedResult]) // eslint-disable-line react-hooks/exhaustive-deps

  const onYearsChange = (years: 1 | 3) => {
    setRenewalYears(years)
    if (pricing) setAmount(years === 1 ? pricing.oneYearFee : pricing.threeYearFee)
  }

  const searchMutation = useMutation({
    mutationFn: async (abn: string) => {
      const r = await api.admin.manualSearch(abn)
      if (!r.success) throw new Error(r.errorMessage ?? 'Failed to search ASIC renewal service.')
      return r.results
    },
    onSuccess: (results) => setSearchResults(results),
  })
  const search = () => {
    setSearchResults([]); setSelectedResult(null)
    if (!searchAbn || searchAbn.length !== 11) {
      sileo.error({ title: 'Please enter a valid 11-digit ABN.' })
      return
    }
    void sileo.promise(searchMutation.mutateAsync(searchAbn), {
      loading: { title: 'Searching ASIC…' },
      success: (results) => ({ title: `Found ${results.length} business name(s) for ABN ${searchAbn}.` }),
      error: (e) => ({ title: 'Search failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const processMutation = useMutation({
    mutationFn: () => api.admin.submitManualRenewal({
      searchResultId: selectedResult!.id,
      renewalYears,
      email: customerEmail,
      mobileNumber: mobileNumber || undefined,
      dateOfBirth: dateOfBirth || undefined,
      amount,
    }),
    onSuccess: () => { setTimeout(() => navigate('/admin/renewals'), 1800) },
    onSettled: () => void queryClient.invalidateQueries({ queryKey: ['admin-renewals'] }),
  })
  const process = () => {
    if (!selectedResult) { sileo.error({ title: 'Please select a business.' }); return }
    if (!customerEmail.trim()) { sileo.error({ title: 'Please enter customer email.' }); return }
    if (amount <= 0) { sileo.error({ title: 'Please enter amount paid by customer.' }); return }

    void sileo.promise(processMutation.mutateAsync(), {
      loading: { title: 'Processing renewal…' },
      success: (r) => ({ title: r.message }),
      error: (e) => ({ title: 'Error processing renewal', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const oneYearDefault = pricing?.oneYearFee.toFixed(2) ?? '—'
  const threeYearDefault = pricing?.threeYearFee.toFixed(2) ?? '—'

  return (
    <div className="mx-auto max-w-3xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="OPS"
        title="Manual renewal"
        subtitle="Process a renewal for a client who paid externally."
      />

      {/* Step 1 */}
      <Step kicker="STEP 01" active title="Search business" subtitle="Look up the ABN at ASIC.">
        <div className="flex gap-2">
          <input
            type="text"
            value={searchAbn}
            onChange={(e) => setSearchAbn(e.target.value.replace(/\D/g, '').slice(0, 11))}
            onKeyDown={(e) => { if (e.key === 'Enter') search() }}
            placeholder="11-digit ABN"
            className={`${inputCls} font-mono tabular-nums flex-1`}
            maxLength={11}
          />
          <button
            onClick={search}
            disabled={searchMutation.isPending}
            className="inline-flex items-center gap-2 rounded-md bg-zinc-900 text-white px-4 py-2 text-sm font-medium hover:bg-zinc-800 disabled:opacity-50 transition self-start mt-1"
          >
            {searchMutation.isPending ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <Search className="h-4 w-4" />
            )}
            Search
          </button>
        </div>

        {searchResults.length > 0 ? (
          <div className="mt-5">
            <KickerLabel className="mb-2">Select business</KickerLabel>
            <div className="space-y-2">
              {searchResults.map((r) => (
                <label key={r.id} className={`flex items-start gap-3 cursor-pointer rounded-lg border p-3 transition ${
                  selectedResult?.id === r.id ? 'border-brand-300 bg-brand-50/40 ring-1 ring-brand-200' : 'border-zinc-200 hover:border-zinc-300'
                }`}>
                  <input type="radio" name="business" value={r.id} checked={selectedResult?.id === r.id} onChange={() => setSelectedResult(r)} className="mt-1 h-4 w-4 border-zinc-300 accent-brand-600 focus:ring-brand-500" />
                  <div className="flex-1 min-w-0">
                    <div className="text-sm font-medium text-zinc-900">{r.businessName}</div>
                    <div className="text-xxs font-mono tabular-nums text-zinc-500">acct {r.accountNumber} · reg {r.registrationDate}</div>
                  </div>
                </label>
              ))}
            </div>
          </div>
        ) : null}
      </Step>

      {/* Step 2 */}
      <Step kicker="STEP 02" active={!!selectedResult} dimmed={!selectedResult} title="Renewal details" subtitle="The ASIC credit card from settings will be used.">
        {selectedResult ? (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label="Renewal years">
              <select value={renewalYears} onChange={(e) => onYearsChange(Number(e.target.value) as 1 | 3)} className={inputCls}>
                <option value={1}>1 year — default ${oneYearDefault}</option>
                <option value={3}>3 years — default ${threeYearDefault}</option>
              </select>
            </Field>
            <Field label="Customer email">
              <input type="email" value={customerEmail} onChange={(e) => setCustomerEmail(e.target.value)} placeholder="customer@example.com" className={inputCls} />
            </Field>
            <Field label="Mobile number">
              <input type="tel" value={mobileNumber} onChange={(e) => setMobileNumber(e.target.value)} placeholder="04XX XXX XXX" className={`${inputCls} font-mono tabular-nums`} />
            </Field>
            <Field label="Date of birth (optional)">
              <input type="date" value={dateOfBirth} onChange={(e) => setDateOfBirth(e.target.value)} className={`${inputCls} font-mono tabular-nums`} />
            </Field>
            <Field label="Amount paid by customer" hint="Pre-filled with default price. Adjust if customer paid different.">
              <div className="relative mt-1">
                <span className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 text-sm text-zinc-400">$</span>
                <input type="number" step="0.01" min="0" value={amount} onChange={(e) => setAmount(Number(e.target.value))} className="block w-full rounded-md border-zinc-300 pl-7 shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 sm:text-sm px-3 py-2 border font-mono tabular-nums" />
              </div>
            </Field>
          </div>
        ) : (
          <p className="text-sm text-zinc-500">Pick a business in step 1 first.</p>
        )}

        {selectedResult ? (
          <div className="mt-6">
            <button
              onClick={process}
              disabled={processMutation.isPending}
              className="inline-flex items-center gap-2 rounded-md bg-brand-600 text-white px-4 py-2 text-sm font-medium hover:bg-brand-700 disabled:opacity-50 shadow-sm transition"
            >
              {processMutation.isPending ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Processing…
                </>
              ) : (
                <>
                  <CircleCheck className="h-4 w-4" />
                  Process manual renewal
                </>
              )}
            </button>
          </div>
        ) : null}
      </Step>
    </div>
  )
}

function Step({ kicker, title, subtitle, active, dimmed, children }: { kicker: string; title: string; subtitle?: string; active: boolean; dimmed?: boolean; children: React.ReactNode }) {
  return (
    <section className={`mt-6 rounded-xl bg-white p-5 ring-1 transition ${dimmed ? 'ring-zinc-100 opacity-70' : active ? 'ring-zinc-200 shadow-sm' : 'ring-zinc-200 shadow-sm'}`}>
      <div className="flex items-end justify-between gap-3 mb-4">
        <div>
          <div className={`text-xxs font-mono font-medium uppercase tracking-[0.16em] ${active ? 'text-brand-700' : 'text-zinc-400'}`}>{kicker}</div>
          <h2 className="mt-0.5 text-base font-semibold text-zinc-900 tracking-tight">{title}</h2>
          {subtitle ? <p className="mt-0.5 text-sm text-zinc-500">{subtitle}</p> : null}
        </div>
      </div>
      {children}
    </section>
  )
}

function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <div>
      <label className={labelCls}>{label}</label>
      {children}
      {hint ? <p className="mt-1 text-xxs font-mono text-zinc-500">{hint}</p> : null}
    </div>
  )
}
