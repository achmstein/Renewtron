import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, type DashboardResponse } from '../api/client'
import AdminPage from './AdminPage'

export default function Dashboard() {
  const [data, setData] = useState<DashboardResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api.admin.dashboard().then(setData).catch((e) => setError(e instanceof Error ? e.message : 'Load failed.'))
  }, [])

  if (error) return <AdminPage title="Dashboard"><div className="rounded-md bg-red-50 p-3 text-sm text-red-700">{error}</div></AdminPage>
  if (!data) return <AdminPage title="Dashboard"><p className="text-gray-500">Loading…</p></AdminPage>

  const { stats, recentSearches, recentRenewals } = data
  const successRate = stats.totalSearches > 0 ? ((stats.successfulSearches * 100) / stats.totalSearches).toFixed(1) : '0'
  const fmtMoney = (n: number) => n.toLocaleString('en-AU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  const fmtDate = (s: string) => {
    const d = new Date(s)
    return d.toLocaleString(undefined, { month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit' })
  }

  return (
    <AdminPage title="Dashboard" subtitle="Operational overview — searches, renewals, leads, and sales at a glance.">
      {/* Stats Cards */}
      <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-4">
        <Link to="/admin/searches" className="overflow-hidden rounded-lg bg-white shadow hover:shadow-lg transition-shadow cursor-pointer">
          <div className="px-4 py-5">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <svg className="h-6 w-6 text-blue-600" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" />
                </svg>
              </div>
              <div className="ml-4 w-0 flex-1">
                <dt className="truncate text-sm font-medium text-gray-500">Total Searches</dt>
                <dd className="mt-1 text-2xl font-semibold text-gray-900">{stats.totalSearches}</dd>
              </div>
            </div>
          </div>
          <div className="bg-gray-50 px-4 py-2">
            <div className="text-sm">
              <span className="font-medium text-blue-600 hover:text-blue-500">View all searches →</span>
            </div>
          </div>
        </Link>

        <div className="overflow-hidden rounded-lg bg-white shadow">
          <div className="px-4 py-5">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <svg className="h-6 w-6 text-green-600" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
              </div>
              <div className="ml-4 w-0 flex-1">
                <dt className="truncate text-sm font-medium text-gray-500">Successful Searches</dt>
                <dd className="mt-1 text-2xl font-semibold text-gray-900">{stats.successfulSearches}</dd>
              </div>
            </div>
          </div>
          <div className="bg-gray-50 px-4 py-2">
            <div className="text-sm text-gray-600">Success Rate: {successRate}%</div>
          </div>
        </div>

        <Link to="/admin/renewals" className="overflow-hidden rounded-lg bg-white shadow hover:shadow-lg transition-shadow cursor-pointer">
          <div className="px-4 py-5">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <svg className="h-6 w-6 text-yellow-600" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />
                </svg>
              </div>
              <div className="ml-4 w-0 flex-1">
                <dt className="truncate text-sm font-medium text-gray-500">Renewals Initiated</dt>
                <dd className="mt-1 text-2xl font-semibold text-gray-900">{stats.renewalsInitiated}</dd>
              </div>
            </div>
          </div>
          <div className="bg-gray-50 px-4 py-2">
            <div className="text-sm">
              <span className="font-medium text-yellow-600 hover:text-yellow-500">View all renewals →</span>
            </div>
          </div>
        </Link>

        <Link to="/admin/leads" className="overflow-hidden rounded-lg bg-white shadow hover:shadow-lg transition-shadow cursor-pointer">
          <div className="px-4 py-5">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <svg className="h-6 w-6 text-purple-600" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M18 18.72a9.094 9.094 0 003.741-.479 3 3 0 00-4.682-2.72m.94 3.198l.001.031c0 .225-.012.447-.037.666A11.944 11.944 0 0112 21c-2.17 0-4.207-.576-5.963-1.584A6.062 6.062 0 016 18.719m12 0a5.971 5.971 0 00-.941-3.197m0 0A5.995 5.995 0 0012 12.75a5.995 5.995 0 00-5.058 2.772m0 0a3 3 0 00-4.681 2.72 8.986 8.986 0 003.74.477m.94-3.197a5.971 5.971 0 00-.94 3.197M15 6.75a3 3 0 11-6 0 3 3 0 016 0zm6 3a2.25 2.25 0 11-4.5 0 2.25 2.25 0 014.5 0zm-13.5 0a2.25 2.25 0 11-4.5 0 2.25 2.25 0 014.5 0z" />
                </svg>
              </div>
              <div className="ml-4 w-0 flex-1">
                <dt className="truncate text-sm font-medium text-gray-500">Total Leads</dt>
                <dd className="mt-1 text-2xl font-semibold text-gray-900">{stats.totalLeads}</dd>
              </div>
            </div>
          </div>
          <div className="bg-gray-50 px-4 py-2">
            <div className="text-sm text-gray-600">
              <span className="text-green-600 font-medium">{stats.convertedLeads} converted</span>
              {' · '}
              <span className="text-amber-600">{stats.notDueLeads} not due</span>
            </div>
          </div>
        </Link>
      </div>

      {/* Sales Comparison (This Month) */}
      <div className="mt-8">
        <h2 className="text-base font-semibold leading-6 text-gray-900 mb-4">Sales Comparison (This Month)</h2>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div className="overflow-hidden rounded-lg bg-white shadow">
            <div className="px-4 py-5">
              <div className="flex items-center">
                <div className="flex-shrink-0">
                  <svg className="h-6 w-6 text-blue-600" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />
                  </svg>
                </div>
                <div className="ml-4 w-0 flex-1">
                  <dt className="truncate text-sm font-medium text-gray-500">Renewtron Direct</dt>
                  <dd className="mt-1 text-2xl font-semibold text-gray-900">{stats.renewtronDirectCount} sales</dd>
                  <dd className="text-sm text-gray-500">${fmtMoney(stats.renewtronDirectRevenue)} revenue</dd>
                </div>
              </div>
            </div>
          </div>

          <Link to="/admin/ontraport-sales" className="overflow-hidden rounded-lg bg-white shadow hover:shadow-lg transition-shadow cursor-pointer">
            <div className="px-4 py-5">
              <div className="flex items-center">
                <div className="flex-shrink-0">
                  <svg className="h-6 w-6 text-purple-600" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 21v-7.5a.75.75 0 01.75-.75h3a.75.75 0 01.75.75V21m-4.5 0H2.36m11.14 0H18m0 0h3.64m-1.39 0V9.349m-16.5 11.65V9.35m0 0a3.001 3.001 0 003.75-.615A2.993 2.993 0 009.75 9.75c.896 0 1.7-.393 2.25-1.016a2.993 2.993 0 002.25 1.016c.896 0 1.7-.393 2.25-1.016a3.001 3.001 0 003.75.614m-16.5 0a3.004 3.004 0 01-.621-4.72L4.318 3.44A1.5 1.5 0 015.378 3h13.243a1.5 1.5 0 011.06.44l1.19 1.189a3 3 0 01-.621 4.72m-13.5 8.65h3.75a.75.75 0 00.75-.75V13.5a.75.75 0 00-.75-.75H6.75a.75.75 0 00-.75.75v3.15c0 .415.336.75.75.75z" />
                  </svg>
                </div>
                <div className="ml-4 w-0 flex-1">
                  <dt className="truncate text-sm font-medium text-gray-500">Ontraport</dt>
                  <dd className="mt-1 text-2xl font-semibold text-gray-900">{stats.ontraportCount} sales</dd>
                  <dd className="text-sm text-gray-500">${fmtMoney(stats.ontraportRevenue)} revenue</dd>
                </div>
              </div>
            </div>
            <div className="bg-gray-50 px-4 py-2">
              <div className="text-sm">
                <span className="font-medium text-purple-600 hover:text-purple-500">View Ontraport sales →</span>
              </div>
            </div>
          </Link>
        </div>
      </div>

      {/* Quick Actions */}
      <div className="mt-8">
        <h2 className="text-base font-semibold leading-6 text-gray-900">Quick Actions</h2>
        <div className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Link to="/admin/searches" className="relative flex items-center space-x-3 rounded-lg border border-gray-300 bg-white px-6 py-5 shadow-sm hover:border-gray-400">
            <div className="flex-shrink-0">
              <svg className="h-6 w-6 text-blue-600" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" />
              </svg>
            </div>
            <div className="min-w-0 flex-1">
              <span className="absolute inset-0" aria-hidden="true" />
              <p className="text-sm font-medium text-gray-900">View Search Logs</p>
              <p className="truncate text-sm text-gray-500">Monitor ABN searches</p>
            </div>
          </Link>

          <Link to="/admin/renewals" className="relative flex items-center space-x-3 rounded-lg border border-gray-300 bg-white px-6 py-5 shadow-sm hover:border-gray-400">
            <div className="flex-shrink-0">
              <svg className="h-6 w-6 text-yellow-600" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />
              </svg>
            </div>
            <div className="min-w-0 flex-1">
              <span className="absolute inset-0" aria-hidden="true" />
              <p className="text-sm font-medium text-gray-900">Manage Renewals</p>
              <p className="truncate text-sm text-gray-500">Retry failed renewals</p>
            </div>
          </Link>
        </div>
      </div>

      {/* Recent Activity */}
      <div className="mt-8">
        <h2 className="text-base font-semibold leading-6 text-gray-900 mb-4">Recent Activity</h2>
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <div className="overflow-hidden rounded-lg bg-white shadow">
            <div className="p-6">
              <div className="flex items-center justify-between mb-4">
                <h3 className="text-sm font-medium text-gray-900">Recent Searches</h3>
                <Link to="/admin/searches" className="text-sm font-medium text-blue-600 hover:text-blue-500">View all →</Link>
              </div>
              <div className="space-y-3">
                {recentSearches.slice(0, 5).map((s) => (
                  <div key={s.id} className="flex items-center justify-between">
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-medium text-gray-900">ABN: {s.abn}</p>
                      <p className="text-xs text-gray-500">{fmtDate(s.searchedAt)} - {s.resultsCount} results</p>
                    </div>
                    <div className="ml-4 flex-shrink-0">
                      {s.success ? (
                        <span className="inline-flex items-center rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-800">
                          <svg className="mr-1 h-3 w-3" fill="currentColor" viewBox="0 0 8 8"><circle cx="4" cy="4" r="3" /></svg>
                          Success
                        </span>
                      ) : (
                        <span className="inline-flex items-center rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium text-red-800">
                          <svg className="mr-1 h-3 w-3" fill="currentColor" viewBox="0 0 8 8"><circle cx="4" cy="4" r="3" /></svg>
                          Failed
                        </span>
                      )}
                    </div>
                  </div>
                ))}
                {recentSearches.length === 0 ? <p className="text-sm text-gray-500">No searches yet.</p> : null}
              </div>
            </div>
          </div>

          <div className="overflow-hidden rounded-lg bg-white shadow">
            <div className="p-6">
              <div className="flex items-center justify-between mb-4">
                <h3 className="text-sm font-medium text-gray-900">Recent Renewals</h3>
                <Link to="/admin/renewals" className="text-sm font-medium text-blue-600 hover:text-blue-500">View all →</Link>
              </div>
              <div className="space-y-3">
                {recentRenewals.slice(0, 5).map((r) => {
                  const pill = (() => {
                    switch (r.status) {
                      case 'Completed': return { cls: 'bg-green-100 text-green-800', label: 'Completed' }
                      case 'Processing': return { cls: 'bg-blue-100 text-blue-800', label: 'Processing' }
                      case 'Pending': return { cls: 'bg-yellow-100 text-yellow-800', label: 'Pending' }
                      case 'Failed': return { cls: 'bg-red-100 text-red-800', label: 'Failed' }
                      default: return { cls: 'bg-gray-100 text-gray-800', label: r.status }
                    }
                  })()
                  return (
                    <div key={r.id} className="flex items-center justify-between">
                      <div className="flex-1 min-w-0">
                        <p className="text-sm font-medium text-gray-900 truncate">{r.businessName ?? '—'}</p>
                        <p className="text-xs text-gray-500">{fmtDate(r.initiatedAt)}</p>
                      </div>
                      <div className="ml-4 flex-shrink-0">
                        <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${pill.cls}`}>
                          <svg className="mr-1 h-3 w-3" fill="currentColor" viewBox="0 0 8 8"><circle cx="4" cy="4" r="3" /></svg>
                          {pill.label}
                        </span>
                      </div>
                    </div>
                  )
                })}
                {recentRenewals.length === 0 ? <p className="text-sm text-gray-500">No renewals yet.</p> : null}
              </div>
            </div>
          </div>
        </div>
      </div>
    </AdminPage>
  )
}
