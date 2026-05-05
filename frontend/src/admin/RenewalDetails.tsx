import { useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'

type Detail = Awaited<ReturnType<typeof api.admin.renewal>>

function fmtDateTime(s: string) {
  const d = new Date(s)
  const date = d.toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })
  const time = d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })
  return `${date} ${time}`
}
function fmtDob(s: string) {
  const d = new Date(s)
  const dd = String(d.getDate()).padStart(2, '0')
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  return `${dd}/${mm}/${d.getFullYear()}`
}

function StatusBadge({ status, big = false }: { status: string; big?: boolean }) {
  const cls = big ? 'inline-flex rounded-full px-3 py-1 text-sm font-semibold leading-5' : 'inline-flex rounded-full px-2 text-xs font-semibold leading-5'
  switch (status) {
    case 'Completed': return <span className={`${cls} bg-green-100 text-green-800`}>Completed</span>
    case 'Processing': return <span className={`${cls} bg-blue-100 text-blue-800`}>Processing</span>
    case 'Pending': return <span className={`${cls} bg-yellow-100 text-yellow-800`}>Pending</span>
    case 'Failed': return <span className={`${cls} bg-red-100 text-red-800`}>Failed</span>
    default: return <span className={`${cls} bg-gray-100 text-gray-700`}>{status}</span>
  }
}

export default function RenewalDetails() {
  const { id } = useParams()
  const [data, setData] = useState<Detail | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)
  const [isRetrying, setIsRetrying] = useState(false)
  const [successMessage, setSuccessMessage] = useState('')
  const [errorMessage, setErrorMessage] = useState('')
  const pollRef = useRef<number | undefined>(undefined)

  const load = async () => {
    if (!id) return
    setIsLoading(true)
    try {
      const r = await api.admin.renewal(id)
      setData(r)
    } catch {
      setNotFound(true)
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => { void load() /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [id])

  useEffect(() => {
    if (!data) return
    if (pollRef.current) { window.clearInterval(pollRef.current); pollRef.current = undefined }
    if (data.status === 'Processing') {
      pollRef.current = window.setInterval(() => { void load() }, 3000)
    }
    return () => {
      if (pollRef.current) { window.clearInterval(pollRef.current); pollRef.current = undefined }
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data?.status])

  const retry = async () => {
    if (!id) return
    setIsRetrying(true); setSuccessMessage(''); setErrorMessage('')
    try {
      const r = await api.admin.retryRenewal(id)
      setSuccessMessage(r.message)
      await load()
    } catch (e) {
      setErrorMessage(e instanceof Error ? e.message : 'Unexpected error')
    } finally {
      setIsRetrying(false)
    }
  }

  const canRetry = data?.status === 'Failed' && ((data.paymentType === 'Stripe' && data.stripePayment?.paymentStatus === 'succeeded') || data.paymentType === 'External')

  return (
    <>
      <header className="relative bg-white shadow-sm">
        <div className="mx-auto max-w-7xl px-4 py-4 sm:px-6 lg:px-8">
          <div className="flex items-center justify-between">
            <h1 className="text-lg/6 font-semibold text-gray-900">Renewal Request Details</h1>
            <Link to="/admin/renewals" className="text-sm text-gray-600 hover:text-gray-900">← Back to Renewals</Link>
          </div>
        </div>
      </header>

      <main>
        <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
          {isLoading ? (
            <div className="text-center py-12">
              <svg className="mx-auto h-12 w-12 animate-spin text-blue-600" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
              </svg>
              <p className="mt-4 text-sm text-gray-600">Loading...</p>
            </div>
          ) : notFound || !data ? (
            <div className="text-center py-12">
              <h3 className="text-lg font-semibold text-gray-900">Renewal Request Not Found</h3>
              <p className="mt-2 text-sm text-gray-600">The renewal request you're looking for doesn't exist.</p>
            </div>
          ) : (
            <>
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

              <div className="mb-6 overflow-hidden rounded-lg bg-white shadow">
                <div className="px-6 py-5">
                  <div className="flex items-center justify-between">
                    <div>
                      <h2 className="text-2xl font-bold text-gray-900">{data.businessName}</h2>
                      <p className="mt-1 text-sm text-gray-500">ABN: {data.abn}</p>
                    </div>
                    <div className="text-right"><StatusBadge status={data.status} big /></div>
                  </div>
                </div>
              </div>

              <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
                <div className="overflow-hidden rounded-lg bg-white shadow">
                  <div className="border-b border-gray-200 bg-gray-50 px-6 py-3">
                    <h3 className="text-base font-semibold text-gray-900">Renewal Information</h3>
                  </div>
                  <div className="px-6 py-4">
                    <dl className="space-y-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Request ID</dt>
                        <dd className="mt-1 text-sm text-gray-900 font-mono break-all">{data.id}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Initiated At</dt>
                        <dd className="mt-1 text-sm text-gray-900">{fmtDateTime(data.initiatedAt)}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Initiated By</dt>
                        <dd className="mt-1 text-sm text-gray-900">{data.initiatedByLabel}</dd>
                      </div>
                      {data.completedAt ? (
                        <div>
                          <dt className="text-sm font-medium text-gray-500">Completed At</dt>
                          <dd className="mt-1 text-sm text-gray-900">{fmtDateTime(data.completedAt)}</dd>
                        </div>
                      ) : null}
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Renewal Period</dt>
                        <dd className="mt-1 text-sm text-gray-900">{data.renewalYears} year{data.renewalYears > 1 ? 's' : ''}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Customer Email</dt>
                        <dd className="mt-1 text-sm text-gray-900">{data.email ?? '—'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Mobile Number</dt>
                        <dd className="mt-1 text-sm text-gray-900">{data.mobileNumber ?? 'N/A'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Date of Birth</dt>
                        <dd className="mt-1 text-sm text-gray-900">{data.dateOfBirth ? fmtDob(data.dateOfBirth) : 'N/A'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">IP Address</dt>
                        <dd className="mt-1 text-sm text-gray-900 font-mono"><span>{data.ipAddress ?? '—'}</span></dd>
                      </div>
                    </dl>
                  </div>
                </div>

                <div className="overflow-hidden rounded-lg bg-white shadow">
                  <div className="border-b border-gray-200 bg-gray-50 px-6 py-3">
                    <h3 className="text-base font-semibold text-gray-900">Payment Information</h3>
                  </div>
                  <div className="px-6 py-4">
                    <dl className="space-y-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Payment Type</dt>
                        <dd className="mt-1">
                          {data.paymentType === 'External'
                            ? <span className="inline-flex rounded-full bg-blue-100 px-2 text-xs font-semibold leading-5 text-blue-800">External Payment</span>
                            : <span className="inline-flex rounded-full bg-purple-100 px-2 text-xs font-semibold leading-5 text-purple-800">Stripe Payment</span>}
                        </dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Amount</dt>
                        <dd className="mt-1 text-lg font-semibold text-gray-900">${data.amount.toFixed(2)}</dd>
                      </div>
                      {data.stripePayment ? (
                        <>
                          <div>
                            <dt className="text-sm font-medium text-gray-500">Stripe Payment Intent</dt>
                            <dd className="mt-1 text-sm text-gray-900 font-mono break-all">{data.stripePayment.paymentIntentId}</dd>
                          </div>
                          <div>
                            <dt className="text-sm font-medium text-gray-500">Stripe Payment Status</dt>
                            <dd className="mt-1">
                              {data.stripePayment.paymentStatus === 'succeeded'
                                ? <span className="inline-flex rounded-full bg-green-100 px-2 text-xs font-semibold leading-5 text-green-800">Succeeded</span>
                                : <span className="inline-flex rounded-full bg-red-100 px-2 text-xs font-semibold leading-5 text-red-800">{data.stripePayment.paymentStatus}</span>}
                            </dd>
                          </div>
                          {data.stripePayment.paidAt ? (
                            <div>
                              <dt className="text-sm font-medium text-gray-500">Paid At</dt>
                              <dd className="mt-1 text-sm text-gray-900">{fmtDateTime(data.stripePayment.paidAt)}</dd>
                            </div>
                          ) : null}
                        </>
                      ) : null}
                    </dl>
                  </div>
                </div>

                <div className="overflow-hidden rounded-lg bg-white shadow">
                  <div className="border-b border-gray-200 bg-gray-50 px-6 py-3">
                    <h3 className="text-base font-semibold text-gray-900">ASIC Transaction Details</h3>
                  </div>
                  <div className="px-6 py-4">
                    <dl className="space-y-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Transaction Reference</dt>
                        <dd className="mt-1 text-sm text-gray-900 font-mono">{data.transactionReference ?? 'N/A'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Hosted Tokenization ID</dt>
                        <dd className="mt-1 text-sm text-gray-900 font-mono break-all">{data.hostedTokenizationId ?? 'N/A'}</dd>
                      </div>
                      {data.errorMessage ? (
                        <div>
                          <dt className="text-sm font-medium text-gray-500">Error Message</dt>
                          <dd className="mt-1 text-sm text-red-600">{data.errorMessage}</dd>
                        </div>
                      ) : null}
                      {data.failedAtStep ? (
                        <div>
                          <dt className="text-sm font-medium text-gray-500">Failed At Step</dt>
                          <dd className="mt-1">
                            <span className="inline-flex items-center rounded-md bg-red-100 px-2.5 py-1 text-xs font-medium text-red-800">{data.failedAtStep}</span>
                          </dd>
                        </div>
                      ) : null}
                    </dl>
                  </div>
                </div>

                {data.stripePayment?.cardLast4 ? (
                  <div className="overflow-hidden rounded-lg bg-white shadow">
                    <div className="border-b border-gray-200 bg-gray-50 px-6 py-3">
                      <h3 className="text-base font-semibold text-gray-900">Customer Credit Card</h3>
                    </div>
                    <div className="px-6 py-4">
                      <dl className="space-y-4">
                        <div>
                          <dt className="text-sm font-medium text-gray-500">Cardholder Name</dt>
                          <dd className="mt-1 text-sm text-gray-900">{data.stripePayment.cardholderName ?? '—'}</dd>
                        </div>
                        <div>
                          <dt className="text-sm font-medium text-gray-500">Card Number</dt>
                          <dd className="mt-1 text-sm text-gray-900 font-mono">**** **** **** {data.stripePayment.cardLast4}</dd>
                        </div>
                        <div>
                          <dt className="text-sm font-medium text-gray-500">Card Brand</dt>
                          <dd className="mt-1 text-sm text-gray-900">{data.stripePayment.cardBrand ?? '—'}</dd>
                        </div>
                        <div>
                          <dt className="text-sm font-medium text-gray-500">Expiry</dt>
                          <dd className="mt-1 text-sm text-gray-900">{data.stripePayment.cardExpMonth}/{data.stripePayment.cardExpYear}</dd>
                        </div>
                      </dl>
                    </div>
                  </div>
                ) : null}
              </div>

              {canRetry ? (
                <div className="mt-6 flex justify-end">
                  <button onClick={retry} disabled={isRetrying} className="inline-flex items-center rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-500 disabled:opacity-50 disabled:cursor-not-allowed">
                    {isRetrying ? (
                      <>
                        <svg className="animate-spin h-4 w-4 mr-2" fill="none" viewBox="0 0 24 24">
                          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                        </svg>
                        <span>Retrying...</span>
                      </>
                    ) : (
                      <>
                        <svg className="h-4 w-4 mr-2" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />
                        </svg>
                        <span>Retry Renewal</span>
                      </>
                    )}
                  </button>
                </div>
              ) : null}
            </>
          )}
        </div>
      </main>
    </>
  )
}
