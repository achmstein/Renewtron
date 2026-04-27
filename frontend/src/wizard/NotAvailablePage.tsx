import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, type LeadDto } from '../api/client'
import GridBackground from '../components/GridBackground'
import UserDetailsSummary from '../components/UserDetailsSummary'

export default function NotAvailablePage() {
  const { leadId } = useParams<{ leadId: string }>()
  const [lead, setLead] = useState<LeadDto | null>(null)

  useEffect(() => {
    if (!leadId) return
    void api.getLead(leadId).then(setLead).catch(() => {})
  }, [leadId])

  return (
    <div className="relative isolate overflow-auto flex-1">
      <GridBackground />
      <div className="mx-auto max-w-7xl px-6 pb-24 pt-10 sm:pb-32 lg:px-8 lg:py-12">
        {lead ? (
          <>
            <div className="mx-auto max-w-lg">
              <UserDetailsSummary abn={lead.abn} fullName={lead.fullName} email={lead.email} mobileNumber={lead.mobileNumber} dateOfBirth={lead.dateOfBirth} />
            </div>

            <div className="mx-auto max-w-lg">
              <div className="rounded-xl bg-white shadow-lg ring-1 ring-gray-200 overflow-hidden">
                {lead.outcome === 'NotDueForRenewal' ? (
                  <div className="bg-amber-50 px-6 py-4 border-b border-amber-100">
                    <div className="flex items-center">
                      <svg className="h-8 w-8 text-amber-400" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
                      </svg>
                      <h3 className="ml-3 text-lg font-semibold text-amber-800">Not Due for Renewal Yet</h3>
                    </div>
                  </div>
                ) : lead.outcome === 'RenewalInProgress' ? (
                  <div className="bg-blue-50 px-6 py-4 border-b border-blue-100">
                    <div className="flex items-center">
                      <svg className="h-8 w-8 text-blue-400" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />
                      </svg>
                      <h3 className="ml-3 text-lg font-semibold text-blue-800">Renewal Already in Progress</h3>
                    </div>
                  </div>
                ) : (
                  <div className="bg-gray-50 px-6 py-4 border-b border-gray-100">
                    <div className="flex items-center">
                      <svg className="h-8 w-8 text-gray-400" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" />
                      </svg>
                      <h3 className="ml-3 text-lg font-semibold text-gray-800">No Business Names Found</h3>
                    </div>
                  </div>
                )}

                <div className="px-6 py-5">
                  <p className="text-sm text-gray-700">
                    {lead.outcomeMessage ||
                      (lead.outcome === 'NotDueForRenewal'
                        ? "This business name isn't due for renewal yet. We'll send you a reminder closer to the renewal window."
                        : lead.outcome === 'RenewalInProgress'
                        ? "A renewal for this ABN is already being processed. Please check your email for updates."
                        : "We couldn't find any business names registered to this ABN. Please double-check the ABN and try again, or contact support.")}
                  </p>
                  <div className="mt-6">
                    <Link to="/" className="inline-flex items-center rounded-md bg-brand px-3.5 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-brand-dark">
                      Try another ABN
                    </Link>
                  </div>
                </div>
              </div>
            </div>
          </>
        ) : (
          <div className="text-center py-12">
            <svg className="mx-auto h-12 w-12 animate-spin text-brand" fill="none" viewBox="0 0 24 24">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
            </svg>
          </div>
        )}
      </div>
    </div>
  )
}
