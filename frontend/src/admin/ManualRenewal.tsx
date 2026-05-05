import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import AdminPage from './AdminPage'
import { Banner, Section } from './_shared'

type SearchResult = { id: string; businessName: string; accountNumber: string; registrationDate: string }

export default function ManualRenewal() {
  const navigate = useNavigate()
  const [pricing, setPricing] = useState<{ oneYearFee: number; threeYearFee: number } | null>(null)

  const [searchAbn, setSearchAbn] = useState('')
  const [isSearching, setIsSearching] = useState(false)
  const [searchResults, setSearchResults] = useState<SearchResult[]>([])
  const [selectedResult, setSelectedResult] = useState<SearchResult | null>(null)

  const [renewalYears, setRenewalYears] = useState<1 | 3>(1)
  const [customerEmail, setCustomerEmail] = useState('')
  const [mobileNumber, setMobileNumber] = useState('')
  const [dateOfBirth, setDateOfBirth] = useState('')
  const [amount, setAmount] = useState(0)

  const [isProcessing, setIsProcessing] = useState(false)
  const [successMessage, setSuccessMessage] = useState('')
  const [errorMessage, setErrorMessage] = useState('')

  useEffect(() => {
    api.pricing().then((p) => {
      setPricing(p)
      setAmount(p.oneYearFee)
    }).catch(() => {})
  }, [])

  const onYearsChange = (years: 1 | 3) => {
    setRenewalYears(years)
    if (pricing) setAmount(years === 1 ? pricing.oneYearFee : pricing.threeYearFee)
  }

  const search = async () => {
    setErrorMessage(''); setSuccessMessage(''); setSearchResults([]); setSelectedResult(null)
    if (!searchAbn || searchAbn.length !== 11) {
      setErrorMessage('Please enter a valid 11-digit ABN')
      return
    }
    setIsSearching(true)
    try {
      const r = await api.admin.manualSearch(searchAbn)
      if (!r.success) {
        setErrorMessage(r.errorMessage ?? 'Failed to search ASIC renewal service')
        return
      }
      setSearchResults(r.results)
      setSuccessMessage(`Found and saved ${r.results.length} business name(s) for ABN ${searchAbn}`)
    } catch (e) {
      setErrorMessage(`Error searching: ${e instanceof Error ? e.message : 'unknown'}`)
    } finally {
      setIsSearching(false)
    }
  }

  const process = async () => {
    setErrorMessage(''); setSuccessMessage('')
    if (!selectedResult) { setErrorMessage('Please select a business'); return }
    if (!customerEmail.trim()) { setErrorMessage('Please enter customer email'); return }
    if (amount <= 0) { setErrorMessage('Please enter amount paid by customer'); return }

    setIsProcessing(true)
    try {
      const r = await api.admin.submitManualRenewal({
        searchResultId: selectedResult.id,
        renewalYears,
        email: customerEmail,
        mobileNumber: mobileNumber || undefined,
        dateOfBirth: dateOfBirth || undefined,
        amount,
      })
      setSuccessMessage(r.message)
      setTimeout(() => navigate('/admin/renewals'), 2000)
    } catch (e) {
      setErrorMessage(`Error processing renewal: ${e instanceof Error ? e.message : 'unknown'}`)
    } finally {
      setIsProcessing(false)
    }
  }

  const oneYearDefault = pricing?.oneYearFee.toFixed(2) ?? '—'
  const threeYearDefault = pricing?.threeYearFee.toFixed(2) ?? '—'

  return (
    <AdminPage
      title="Manual Renewal"
      subtitle="File a renewal for a client who paid externally — pulls fresh ASIC data, then queues with the configured ASIC card."
      classification="Operations · Manual"
    >
      <div className="space-y-6">
        {successMessage ? <Banner kind="ok" message={successMessage} /> : null}
        {errorMessage ? <Banner kind="fail" message={errorMessage} /> : null}

        {/* Step 1: Search */}
        <Section kicker="01" title="Locate Business">
          <label htmlFor="abn" className="bureau-label mb-1.5 block">ABN</label>
          <div className="flex gap-2">
            <input
              id="abn"
              type="text"
              value={searchAbn}
              onChange={(e) => setSearchAbn(e.target.value)}
              className="bureau-input flex-1"
              placeholder="11-digit ABN"
              maxLength={11}
            />
            <button onClick={search} disabled={isSearching} className="bureau-btn bureau-btn-primary">
              {isSearching ? <><span className="bureau-spin inline-block">↻</span> Searching</> : '⌕ Search'}
            </button>
          </div>

          {searchResults.length > 0 ? (
            <div className="mt-5">
              <div className="bureau-label mb-3">Select Business</div>
              <ul className="divide-y divide-[var(--hairline)] border border-[var(--hairline)]" style={{ background: 'var(--card)' }}>
                {searchResults.map((r) => {
                  const active = selectedResult?.id === r.id
                  return (
                    <li key={r.id}>
                      <label
                        className="flex items-start gap-4 px-4 py-3 cursor-pointer transition-colors"
                        style={{ background: active ? 'var(--paper-deep)' : 'transparent' }}
                      >
                        <input
                          type="radio"
                          name="business"
                          value={r.id}
                          checked={active}
                          onChange={() => setSelectedResult(r)}
                          className="mt-1 h-4 w-4"
                          style={{ accentColor: 'var(--stamp)' }}
                        />
                        <div className="flex-1 min-w-0">
                          <div className="bureau-display text-[14px] font-semibold" style={{ color: 'var(--ink)' }}>{r.businessName}</div>
                          <div className="bureau-mono text-[11px] mt-0.5" style={{ color: 'var(--ledger)' }}>
                            Account {r.accountNumber} · Registered {r.registrationDate}
                          </div>
                        </div>
                      </label>
                    </li>
                  )
                })}
              </ul>
            </div>
          ) : null}
        </Section>

        {/* Step 2: Renewal Details */}
        {selectedResult ? (
          <Section kicker="02" title="Renewal Details">
            <p className="bureau-meta mb-5">Configured ASIC credit card from settings will be used to file this renewal.</p>

            <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
              <div>
                <label htmlFor="renewalYears" className="bureau-label mb-1.5 block">Renewal Period</label>
                <select id="renewalYears" value={renewalYears} onChange={(e) => onYearsChange(Number(e.target.value) as 1 | 3)} className="bureau-select">
                  <option value={1}>1 Year — Default ${oneYearDefault}</option>
                  <option value={3}>3 Years — Default ${threeYearDefault}</option>
                </select>
              </div>

              <div>
                <label htmlFor="email" className="bureau-label mb-1.5 block">Customer Email</label>
                <input id="email" type="email" value={customerEmail} onChange={(e) => setCustomerEmail(e.target.value)} className="bureau-input" placeholder="customer@example.com" />
              </div>

              <div>
                <label htmlFor="mobileNumber" className="bureau-label mb-1.5 block">Mobile Number</label>
                <input id="mobileNumber" type="tel" value={mobileNumber} onChange={(e) => setMobileNumber(e.target.value)} className="bureau-input" placeholder="04XX XXX XXX" />
              </div>

              <div>
                <label htmlFor="dateOfBirth" className="bureau-label mb-1.5 block">Date of Birth · Optional</label>
                <input id="dateOfBirth" type="date" value={dateOfBirth} onChange={(e) => setDateOfBirth(e.target.value)} className="bureau-input" />
              </div>

              <div className="sm:col-span-2">
                <label htmlFor="amount" className="bureau-label mb-1.5 block">Amount Paid · AUD</label>
                <div className="relative">
                  <span className="bureau-mono pointer-events-none absolute inset-y-0 left-3 flex items-center text-[13px]" style={{ color: 'var(--ledger)' }}>$</span>
                  <input id="amount" type="number" step="0.01" min="0" value={amount} onChange={(e) => setAmount(Number(e.target.value))} className="bureau-input pl-6" />
                </div>
                <p className="bureau-meta mt-1.5">Pre-filled with default price. Override if customer paid differently.</p>
              </div>
            </div>

            <div className="mt-6 border-t border-[var(--hairline)] pt-5">
              <button onClick={process} disabled={isProcessing} className="bureau-btn bureau-btn-primary">
                {isProcessing ? <><span className="bureau-spin inline-block">↻</span> Processing</> : '✓ File Renewal'}
              </button>
            </div>
          </Section>
        ) : null}
      </div>
    </AdminPage>
  )
}
