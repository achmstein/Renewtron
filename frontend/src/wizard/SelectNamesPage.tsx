import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, type BusinessNameDto, type LeadDto, type PricingResponse } from '../api/client'
import GridBackground from '../components/GridBackground'
import UserDetailsSummary from '../components/UserDetailsSummary'
import WizardProgress from '../components/WizardProgress'
import { FunnelStep, trackStep } from '../lib/tracking'

const steps = [
  { label: 'ABN' }, { label: 'Details' }, { label: 'Check' }, { label: 'Select' }, { label: 'Pay' },
]

export default function SelectNamesPage() {
  const navigate = useNavigate()
  const { leadId } = useParams<{ leadId: string }>()
  const [lead, setLead] = useState<LeadDto | null>(null)
  const [businessNames, setBusinessNames] = useState<BusinessNameDto[]>([])
  const [pricing, setPricing] = useState<PricingResponse | null>(null)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [years, setYears] = useState<1 | 3>(1)

  useEffect(() => {
    if (!leadId) return
    void api.getLead(leadId).then((l) => {
      setLead(l)
      setBusinessNames(l.businessNames)
      setSelected(new Set(l.businessNames.map((b) => b.id)))
      trackStep(FunnelStep.SelectViewed, { leadId, abn: l.abn, detail: `${l.businessNames.length} name(s)` })
    }).catch(() => navigate('/'))
    void api.pricing().then(setPricing).catch(() => {})
  }, [leadId, navigate])

  const toggle = (id: string) => {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id); else next.add(id)
      return next
    })
  }
  const allSelected = businessNames.length > 0 && businessNames.every((b) => selected.has(b.id))
  const toggleAll = () => {
    setSelected(allSelected ? new Set() : new Set(businessNames.map((b) => b.id)))
  }

  const pricePerItem = pricing ? (years === 1 ? pricing.oneYearFee : pricing.threeYearFee) : 0
  const total = pricePerItem * selected.size

  const proceed = () => {
    if (!leadId || selected.size === 0) return
    const ids = Array.from(selected).join(',')
    trackStep(FunnelStep.SelectSubmitted, {
      leadId,
      abn: lead?.abn,
      detail: `${selected.size} name(s), ${years}yr`,
      value: total,
    })
    navigate(`/payment/${leadId}?ids=${ids}&years=${years}`)
  }

  return (
    <div className="relative isolate overflow-auto flex-1">
      <GridBackground />
      <div className="mx-auto max-w-7xl px-6 pb-24 pt-10 sm:pb-32 lg:px-8 lg:py-12">
        <WizardProgress
          currentStep={4}
          steps={steps}
          onStepClick={(s) => {
            if (s === 1) navigate('/')
            else if (s === 2 && lead) navigate(`/details?abn=${lead.abn}`)
          }}
        />

        {lead ? (
          <>
            <div className="mx-auto max-w-2xl">
              <UserDetailsSummary
                abn={lead.abn}
                fullName={lead.fullName}
                email={lead.email}
                mobileNumber={lead.mobileNumber}
                dateOfBirth={lead.dateOfBirth}
                showEditButton
                onEditClick={() => navigate(`/details?abn=${lead.abn}`)}
              />
            </div>

            <div className="mx-auto max-w-2xl">
              <div className="text-center mb-8">
                <h1 className="text-2xl font-bold tracking-tight text-gray-900 sm:text-3xl">Select business names to renew</h1>
                <p className="mt-2 text-sm text-gray-600">Choose which business names you'd like to renew</p>
              </div>

              {businessNames.length > 0 ? (
                <>
                  <div className="flex items-center justify-between mb-4">
                    <label className="flex items-center gap-2 cursor-pointer">
                      <input type="checkbox" checked={allSelected} onChange={toggleAll} className="h-4 w-4 rounded border-gray-300 accent-brand focus:ring-brand" />
                      <span className="text-sm font-medium text-gray-700">Select all</span>
                    </label>
                    {selected.size > 0 ? <span className="text-sm text-gray-500">{selected.size} selected</span> : null}
                  </div>

                  <div className="space-y-3 mb-8">
                    {businessNames.map((b) => {
                      const isSelected = selected.has(b.id)
                      return (
                        <div
                          key={b.id}
                          className={`overflow-hidden rounded-lg bg-white shadow ring-1 ${isSelected ? 'ring-brand ring-2' : 'ring-gray-200'} cursor-pointer transition-all hover:shadow-md`}
                          onClick={() => toggle(b.id)}
                        >
                          <div className="px-5 py-4">
                            <div className="flex items-start gap-4">
                              <div className="flex items-center pt-0.5">
                                <input
                                  type="checkbox"
                                  checked={isSelected}
                                  onChange={() => toggle(b.id)}
                                  onClick={(e) => e.stopPropagation()}
                                  className="h-5 w-5 rounded border-gray-300 accent-brand focus:ring-brand cursor-pointer"
                                />
                              </div>
                              <div className="flex-1 min-w-0">
                                <h3 className="text-base font-semibold text-gray-900 truncate">{b.businessName}</h3>
                                <div className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-xs text-gray-500">
                                  <span>Account: {b.accountNumber}</span>
                                  <span>Registered: {b.registrationDate}</span>
                                </div>
                              </div>
                            </div>
                          </div>
                        </div>
                      )
                    })}
                  </div>

                  <div className="rounded-lg bg-white shadow ring-1 ring-gray-200 p-5 mb-6">
                    <h3 className="text-sm font-semibold text-gray-900 mb-4">Select renewal period</h3>
                    <div className="grid grid-cols-2 gap-4">
                      {([1, 3] as const).map((y) => {
                        const isActive = years === y
                        return (
                          <label
                            key={y}
                            className={`relative flex cursor-pointer rounded-lg border ${isActive ? 'border-brand ring-2 ring-brand' : 'border-gray-200'} bg-white p-4 shadow-sm focus:outline-none hover:border-gray-300 transition-all`}
                          >
                            <input type="radio" name="renewal-period" value={y} checked={isActive} onChange={() => setYears(y)} className="sr-only" />
                            <div className="flex flex-1 flex-col">
                              <div className="flex items-center gap-2">
                                <span className="text-sm font-semibold text-gray-900">{y === 1 ? '1 Year' : '3 Years'}</span>
                                {y === 3 ? <span className="inline-flex items-center rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700">Save</span> : null}
                              </div>
                              <span className="mt-1 text-2xl font-bold text-gray-900">${pricing ? (y === 1 ? pricing.oneYearFee.toFixed(0) : pricing.threeYearFee.toFixed(0)) : '—'}</span>
                              <span className="mt-1 text-xs text-gray-500">per business name</span>
                            </div>
                            {isActive ? (
                              <svg className="h-5 w-5 text-brand" viewBox="0 0 20 20" fill="currentColor">
                                <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clipRule="evenodd" />
                              </svg>
                            ) : null}
                          </label>
                        )
                      })}
                    </div>
                  </div>

                  {selected.size > 0 ? (
                    <div className="rounded-lg bg-gray-50 border border-gray-200 p-5 mb-6">
                      <h3 className="text-sm font-semibold text-gray-900 mb-3">Order Summary</h3>
                      <div className="space-y-2 text-sm">
                        <div className="flex justify-between text-gray-600">
                          <span>{selected.size} business name{selected.size > 1 ? 's' : ''} x {years} year{years > 1 ? 's' : ''}</span>
                          <span>${pricePerItem.toFixed(2)} each</span>
                        </div>
                        <div className="border-t border-gray-200 pt-2 mt-2 flex justify-between font-semibold text-gray-900">
                          <span>Total</span>
                          <span>${total.toFixed(2)}</span>
                        </div>
                      </div>
                    </div>
                  ) : null}

                  <button
                    onClick={proceed}
                    disabled={selected.size === 0}
                    className="w-full flex justify-center items-center rounded-md bg-brand px-4 py-3 text-sm font-semibold text-white shadow-sm hover:bg-brand-dark disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:bg-brand transition-colors"
                  >
                    Proceed to Payment
                    <svg className="ml-2 h-5 w-5" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3" />
                    </svg>
                  </button>

                  <div className="text-center mt-4">
                    <button type="button" onClick={() => navigate(`/details?abn=${lead.abn}`)} className="text-sm font-medium text-gray-600 hover:text-gray-500">
                      <svg className="inline-block mr-1 h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="M10.5 19.5L3 12m0 0l7.5-7.5M3 12h18" />
                      </svg>
                      Back
                    </button>
                  </div>
                </>
              ) : (
                <div className="text-center py-12">
                  <svg className="mx-auto h-12 w-12 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                  </svg>
                  <h3 className="mt-4 text-lg font-semibold text-gray-900">No business names found</h3>
                  <p className="mt-2 text-sm text-gray-600">We couldn't find any business names to renew.</p>
                  <div className="mt-6">
                    <button onClick={() => navigate('/')} className="inline-flex items-center rounded-md bg-brand px-3.5 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-brand-dark">
                      Try another ABN
                    </button>
                  </div>
                </div>
              )}
            </div>
          </>
        ) : (
          <div className="text-center py-12">
            <svg className="mx-auto h-12 w-12 animate-spin text-brand" fill="none" viewBox="0 0 24 24">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
            </svg>
          </div>
        )}
      </div>
    </div>
  )
}
