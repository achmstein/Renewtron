import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'

type Item = {
  id: string
  abn: string
  fullName: string
  email: string
  mobileNumber: string
  createdAt: string
  outcome: string
  reminderOptIn: boolean
  convertedToRenewal: boolean
}

function getInitials(name: string) {
  if (!name) return '?'
  const parts = name.split(' ').filter(Boolean)
  if (parts.length >= 2) return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
  return name.slice(0, 2).toUpperCase()
}

function formatAbn(abn: string) {
  const clean = (abn ?? '').replace(/\s+/g, '')
  if (clean.length === 11) return `${clean.slice(0, 2)} ${clean.slice(2, 5)} ${clean.slice(5, 8)} ${clean.slice(8)}`
  return abn
}

function fmtDate(s: string) {
  const d = new Date(s)
  return d.toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })
}
function fmtTime(s: string) {
  const d = new Date(s)
  return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hour12: false })
}

export default function Leads() {
  const [outcomeFilter, setOutcomeFilter] = useState('')
  const [reminderFilter, setReminderFilter] = useState('')
  const [searchTerm, setSearchTerm] = useState('')
  const [data, setData] = useState<{ totalCount: number; convertedCount: number; notDueCount: number; reminderCount: number; items: Item[] } | null>(null)

  const load = useMemo(() => async () => {
    const r = await api.admin.leads({
      outcome: outcomeFilter || undefined,
      reminder: reminderFilter || undefined,
      search: searchTerm || undefined,
    })
    setData(r)
  }, [outcomeFilter, reminderFilter, searchTerm])

  useEffect(() => { void load() }, [load])

  const totalCount = data?.totalCount ?? 0
  const convertedCount = data?.convertedCount ?? 0
  const notDueCount = data?.notDueCount ?? 0
  const reminderCount = data?.reminderCount ?? 0
  const leads = data?.items ?? []

  return (
    <>
      <header className="relative bg-white shadow-sm">
        <div className="mx-auto max-w-7xl px-4 py-4 sm:px-6 lg:px-8">
          <div className="flex items-center justify-between">
            <h1 className="text-lg/6 font-semibold text-gray-900">Leads</h1>
            <div className="flex items-center gap-4 text-sm text-gray-500">
              <span><span className="font-medium text-gray-900">{totalCount}</span> total</span>
              <span className="text-gray-300">|</span>
              <span className="text-green-600"><span className="font-medium">{convertedCount}</span> converted</span>
              <span className="text-gray-300">|</span>
              <span className="text-amber-600"><span className="font-medium">{notDueCount}</span> not due</span>
              <span className="text-gray-300">|</span>
              <span className="text-blue-600"><span className="font-medium">{reminderCount}</span> reminders</span>
            </div>
          </div>
        </div>
      </header>

      <main>
        <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
          {/* Filters */}
          <div className="mb-6 flex flex-wrap items-end gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Outcome</label>
              <select value={outcomeFilter} onChange={(e) => setOutcomeFilter(e.target.value)} className="rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm">
                <option value="">All Outcomes</option>
                <option value="RenewalCompleted">Converted</option>
                <option value="RenewalAvailable">Available</option>
                <option value="NotDueForRenewal">Not Due</option>
                <option value="RenewalInProgress">In Progress</option>
                <option value="NoBusinessNames">No Names</option>
                <option value="Pending">Pending</option>
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Reminder</label>
              <select value={reminderFilter} onChange={(e) => setReminderFilter(e.target.value)} className="rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm">
                <option value="">All</option>
                <option value="true">Opted In</option>
                <option value="false">Not Opted In</option>
              </select>
            </div>
            <div className="flex-1 min-w-[200px]">
              <label className="block text-sm font-medium text-gray-700 mb-1">Search</label>
              <input
                type="text"
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                placeholder="ABN, name, or email..."
                className="w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm"
              />
            </div>
          </div>

          {/* Leads Table */}
          <div className="overflow-hidden rounded-lg bg-white shadow">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th scope="col" className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Lead</th>
                  <th scope="col" className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">ABN</th>
                  <th scope="col" className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Outcome</th>
                  <th scope="col" className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Reminder</th>
                  <th scope="col" className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Created</th>
                  <th scope="col" className="relative px-6 py-3"><span className="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200 bg-white">
                {!leads.length ? (
                  <tr>
                    <td colSpan={6} className="px-6 py-12 text-center text-sm text-gray-500">No leads found</td>
                  </tr>
                ) : leads.map((lead) => (
                  <tr key={lead.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="flex items-center">
                        <div className="flex-shrink-0 h-10 w-10">
                          <div className="h-10 w-10 rounded-full bg-gray-200 flex items-center justify-center">
                            <span className="text-sm font-medium text-gray-600">{getInitials(lead.fullName)}</span>
                          </div>
                        </div>
                        <div className="ml-4">
                          <div className="text-sm font-medium text-gray-900">{lead.fullName}</div>
                          <div className="text-sm text-gray-500">{lead.email}</div>
                          <div className="text-xs text-gray-400">{lead.mobileNumber}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="text-sm text-gray-900 font-mono">{formatAbn(lead.abn)}</div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <OutcomeBadge outcome={lead.outcome} />
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {lead.reminderOptIn ? (
                        <span className="inline-flex items-center text-green-600">
                          <svg className="h-4 w-4" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clipRule="evenodd" />
                          </svg>
                        </span>
                      ) : (
                        <span className="text-gray-400">-</span>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      <div>{fmtDate(lead.createdAt)}</div>
                      <div className="text-xs text-gray-400">{fmtTime(lead.createdAt)}</div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                      <Link to={`/admin/leads/${lead.id}`} className="text-blue-600 hover:text-blue-900">View</Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </main>
    </>
  )
}

function OutcomeBadge({ outcome }: { outcome: string }) {
  switch (outcome) {
    case 'RenewalCompleted':
      return <span className="inline-flex items-center rounded-full bg-green-100 px-2.5 py-0.5 text-xs font-medium text-green-800">Converted</span>
    case 'RenewalAvailable':
      return <span className="inline-flex items-center rounded-full bg-blue-100 px-2.5 py-0.5 text-xs font-medium text-blue-800">Available</span>
    case 'NotDueForRenewal':
      return <span className="inline-flex items-center rounded-full bg-amber-100 px-2.5 py-0.5 text-xs font-medium text-amber-800">Not Due</span>
    case 'RenewalInProgress':
      return <span className="inline-flex items-center rounded-full bg-purple-100 px-2.5 py-0.5 text-xs font-medium text-purple-800">In Progress</span>
    case 'NoBusinessNames':
      return <span className="inline-flex items-center rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-800">No Names</span>
    default:
      return <span className="inline-flex items-center rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-600">Pending</span>
  }
}
