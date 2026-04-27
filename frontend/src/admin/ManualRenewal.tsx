import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'

type SearchResult = { id: string; businessName: string; accountNumber: string; registrationDate: string }

export default function ManualRenewal() {
  const navigate = useNavigate()
  const [pricing, setPricing] = useState<{ oneYearFee: number; threeYearFee: number } | null>(null)

  // Search step
  const [searchAbn, setSearchAbn] = useState('')
  const [isSearching, setIsSearching] = useState(false)
  const [searchResults, setSearchResults] = useState<SearchResult[]>([])
  const [selectedResult, setSelectedResult] = useState<SearchResult | null>(null)

  // Renewal details
  const [renewalYears, setRenewalYears] = useState<1 | 3>(1)
  const [customerEmail, setCustomerEmail] = useState('')
  const [mobileNumber, setMobileNumber] = useState('')
  const [dateOfBirth, setDateOfBirth] = useState('')
  const [amount, setAmount] = useState(0)

  // UX
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
    <>
      <header className="relative bg-white shadow-sm">
        <div className="mx-auto max-w-7xl px-4 py-4 sm:px-6 lg:px-8">
          <h1 className="text-lg/6 font-semibold text-gray-900">Manual Renewal</h1>
          <p className="mt-1 text-sm text-gray-500">Process renewal for a client who paid externally</p>
        </div>
      </header>

      <main>
        <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
          {successMessage ? (
            <div className="mb-4 rounded-md bg-green-50 p-4">
              <div className="flex">
                <div className="shrink-0">
                  <svg className="h-5 w-5 text-green-400" viewBox="0 0 20 20" fill="currentColor">
                    <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clipRule="evenodd" />
                  </svg>
                </div>
                <div className="ml-3"><p className="text-sm font-medium text-green-800">{successMessage}</p></div>
              </div>
            </div>
          ) : null}
          {errorMessage ? (
            <div className="mb-4 rounded-md bg-red-50 p-4">
              <div className="flex">
                <div className="shrink-0">
                  <svg className="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
                    <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.28 7.22a.75.75 0 00-1.06 1.06L8.94 10l-1.72 1.72a.75.75 0 101.06 1.06L10 11.06l1.72 1.72a.75.75 0 101.06-1.06L11.06 10l1.72-1.72a.75.75 0 00-1.06-1.06L10 8.94 8.28 7.22z" clipRule="evenodd" />
                  </svg>
                </div>
                <div className="ml-3"><p className="text-sm font-medium text-red-800">{errorMessage}</p></div>
              </div>
            </div>
          ) : null}

          {/* Step 1: Search */}
          <div className="mb-6 overflow-hidden rounded-lg bg-white shadow">
            <div className="px-4 py-5 sm:p-6">
              <h3 className="text-base font-semibold leading-6 text-gray-900">Step 1: Search Business</h3>
              <div className="mt-4">
                <label htmlFor="abn" className="block text-sm font-medium text-gray-700">ABN</label>
                <div className="mt-1 flex rounded-md">
                  <input
                    id="abn"
                    type="text"
                    value={searchAbn}
                    onChange={(e) => setSearchAbn(e.target.value)}
                    className="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm px-3 py-2"
                    placeholder="Enter 11-digit ABN"
                    maxLength={11}
                  />
                  <button onClick={search} disabled={isSearching} className="ml-3 inline-flex items-center rounded-md bg-blue-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-500 disabled:opacity-50 disabled:cursor-not-allowed">
                    {isSearching ? (
                      <>
                        <svg className="animate-spin h-4 w-4 mr-2" fill="none" viewBox="0 0 24 24">
                          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                        </svg>
                        <span>Searching...</span>
                      </>
                    ) : (
                      <>
                        <svg className="h-4 w-4 mr-2" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" />
                        </svg>
                        <span>Search</span>
                      </>
                    )}
                  </button>
                </div>
              </div>

              {searchResults.length > 0 ? (
                <div className="mt-4">
                  <label className="block text-sm font-medium text-gray-700">Select Business</label>
                  <div className="mt-2 space-y-2">
                    {searchResults.map((r) => (
                      <div key={r.id} className="flex items-start">
                        <div className="flex h-6 items-center">
                          <input type="radio" name="business" value={r.id} checked={selectedResult?.id === r.id} onChange={() => setSelectedResult(r)} className="h-4 w-4 border-gray-300 accent-blue-600 focus:ring-blue-600" />
                        </div>
                        <div className="ml-3 text-sm leading-6">
                          <label className="font-medium text-gray-900">{r.businessName}</label>
                          <p className="text-gray-500">Account: {r.accountNumber} | Registration: {r.registrationDate}</p>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              ) : null}
            </div>
          </div>

          {/* Step 2: Renewal Details */}
          {selectedResult ? (
            <div className="mb-6 overflow-hidden rounded-lg bg-white shadow">
              <div className="px-4 py-5 sm:p-6">
                <h3 className="text-base font-semibold leading-6 text-gray-900">Step 2: Renewal Details</h3>
                <p className="mt-1 text-sm text-gray-500">The ASIC credit card from settings will be used to process this renewal</p>

                <div className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div>
                    <label htmlFor="renewalYears" className="block text-sm font-medium text-gray-700">Renewal Years</label>
                    <select id="renewalYears" value={renewalYears} onChange={(e) => onYearsChange(Number(e.target.value) as 1 | 3)} className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm px-3 py-2">
                      <option value={1}>1 Year - Default: ${oneYearDefault}</option>
                      <option value={3}>3 Years - Default: ${threeYearDefault}</option>
                    </select>
                  </div>

                  <div>
                    <label htmlFor="email" className="block text-sm font-medium text-gray-700">Customer Email</label>
                    <input id="email" type="email" value={customerEmail} onChange={(e) => setCustomerEmail(e.target.value)} className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm px-3 py-2" placeholder="customer@example.com" />
                  </div>

                  <div>
                    <label htmlFor="mobileNumber" className="block text-sm font-medium text-gray-700">Mobile Number</label>
                    <input id="mobileNumber" type="tel" value={mobileNumber} onChange={(e) => setMobileNumber(e.target.value)} className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm px-3 py-2" placeholder="04XX XXX XXX" />
                  </div>

                  <div>
                    <label htmlFor="dateOfBirth" className="block text-sm font-medium text-gray-700">Date of Birth <span className="text-gray-400 font-normal">(Optional)</span></label>
                    <input id="dateOfBirth" type="date" value={dateOfBirth} onChange={(e) => setDateOfBirth(e.target.value)} className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm px-3 py-2" />
                  </div>

                  <div>
                    <label htmlFor="amount" className="block text-sm font-medium text-gray-700">Amount Paid by Customer</label>
                    <div className="relative mt-1 rounded-md shadow-sm">
                      <div className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3">
                        <span className="text-gray-500 sm:text-sm">$</span>
                      </div>
                      <input id="amount" type="number" step="0.01" min="0" value={amount} onChange={(e) => setAmount(Number(e.target.value))} className="block w-full rounded-md border-gray-300 pl-7 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm px-3 py-2" />
                    </div>
                    <p className="mt-1 text-xs text-gray-500">Pre-filled with default price. Adjust if customer paid a different amount.</p>
                  </div>
                </div>

                <div className="mt-6">
                  <button onClick={process} disabled={isProcessing} className="inline-flex items-center rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-500 disabled:opacity-50 disabled:cursor-not-allowed">
                    {isProcessing ? (
                      <>
                        <svg className="animate-spin h-4 w-4 mr-2" fill="none" viewBox="0 0 24 24">
                          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                        </svg>
                        <span>Processing...</span>
                      </>
                    ) : (
                      <>
                        <svg className="h-4 w-4 mr-2" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                        </svg>
                        <span>Process Manual Renewal</span>
                      </>
                    )}
                  </button>
                </div>
              </div>
            </div>
          ) : null}
        </div>
      </main>
    </>
  )
}
