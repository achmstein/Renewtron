import { useEffect, useState } from 'react'
import { api } from '../api/client'

type Tab = 'SendGrid' | 'Stripe' | 'Pricing' | 'Asic' | 'Ontraport' | 'AtoAgent'

const tabClass = (active: boolean) =>
  `${active ? 'border-blue-500 text-blue-600' : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-700'} whitespace-nowrap border-b-2 px-1 py-4 text-sm font-medium`

const inputCls = 'mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm px-3 py-2 border'

type Settings = Awaited<ReturnType<typeof api.admin.settings>>

export default function Settings() {
  const [activeTab, setActiveTab] = useState<Tab>('SendGrid')
  const [data, setData] = useState<Settings | null>(null)
  const [successMessage, setSuccessMessage] = useState('')
  const [errorMessage, setErrorMessage] = useState('')

  // Form state mirrors the original separate models
  const [sg, setSg] = useState({ apiKey: '', fromEmail: '', fromName: '' })
  const [stripe, setStripe] = useState({ secretKey: '', publishableKey: '' })
  const [pricing, setPricing] = useState({ oneYearFee: 0, threeYearFee: 0 })
  const [asic, setAsic] = useState({ forceFallback: false, email: '', cardNumber: '', cardholderName: '', expiryMonth: '', expiryYear: '', cvc: '' })
  const [ontraport, setOntraport] = useState({ apiAppId: '', apiKey: '', conversationId: '' })
  const [atoAgent, setAtoAgent] = useState({ defaultAgentAbn: '', defaultAgentName: '' })

  // Live ATO session — populates the agent dropdown
  const [agentList, setAgentList] = useState<Array<{ abn: string; name: string }>>([])
  const [agentSession, setAgentSession] = useState<{ authenticated: boolean; phase: string; loaded: boolean; error?: string }>({ authenticated: false, phase: '', loaded: false })

  const load = async () => {
    const r = await api.admin.settings()
    setData(r)
    setSg(r.sendGrid)
    setStripe(r.stripe)
    setPricing(r.pricing)
    setAsic(r.asic)
    setOntraport(r.ontraport)
    setAtoAgent(r.atoAgent)
  }
  useEffect(() => { void load() }, [])

  const loadAgents = async () => {
    setAgentSession(s => ({ ...s, loaded: false, error: undefined }))
    try {
      const r = await api.admin.atoAgents()
      setAgentList(r.agents)
      setAgentSession({ authenticated: r.authenticated, phase: r.phase, loaded: true })
    } catch (e) {
      setAgentSession({ authenticated: false, phase: '', loaded: true, error: e instanceof Error ? e.message : 'Failed to load agents' })
    }
  }
  useEffect(() => {
    if (activeTab === 'AtoAgent' && !agentSession.loaded) void loadAgents()
  }, [activeTab])  // eslint-disable-line react-hooks/exhaustive-deps

  const flash = (label: string, action: () => Promise<void>) => async () => {
    setSuccessMessage(''); setErrorMessage('')
    try {
      await action()
      setSuccessMessage(`${label} settings saved successfully!`)
    } catch (e) {
      setErrorMessage(`Error saving ${label} settings: ${e instanceof Error ? e.message : 'unknown'}`)
    }
  }

  const onSendGrid = (e: React.FormEvent) => { e.preventDefault(); void flash('SendGrid', () => api.admin.updateSendGrid(sg))() }
  const onStripe = (e: React.FormEvent) => { e.preventDefault(); void flash('Stripe', () => api.admin.updateStripe(stripe))() }
  const onPricing = (e: React.FormEvent) => { e.preventDefault(); void flash('Pricing', () => api.admin.updatePricing(pricing))() }
  const onAsic = (e: React.FormEvent) => { e.preventDefault(); void flash('ASIC', () => api.admin.updateAsic(asic))() }
  const onOntraport = (e: React.FormEvent) => { e.preventDefault(); void flash('Ontraport', () => api.admin.updateOntraport(ontraport))() }
  const onAtoAgent = (e: React.FormEvent) => { e.preventDefault(); void flash('ATO Agent', () => api.admin.updateAtoAgent(atoAgent))() }

  return (
    <>
      <header className="relative bg-white shadow-sm">
        <div className="mx-auto max-w-7xl px-4 py-4 sm:px-6 lg:px-8">
          <h1 className="text-lg/6 font-semibold text-gray-900">Settings</h1>
          <p className="mt-1 text-sm text-gray-500">Configure SendGrid, Stripe, ASIC credentials, pricing, and Ontraport integration.</p>
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

          {/* Tabs */}
          <div className="mb-6">
            <div className="border-b border-gray-200">
              <nav className="-mb-px flex space-x-8">
                <button onClick={() => setActiveTab('SendGrid')} className={tabClass(activeTab === 'SendGrid')}>SendGrid</button>
                <button onClick={() => setActiveTab('Stripe')} className={tabClass(activeTab === 'Stripe')}>Stripe</button>
                <button onClick={() => setActiveTab('Pricing')} className={tabClass(activeTab === 'Pricing')}>Pricing</button>
                <button onClick={() => setActiveTab('Asic')} className={tabClass(activeTab === 'Asic')}>ASIC Settings</button>
                <button onClick={() => setActiveTab('Ontraport')} className={tabClass(activeTab === 'Ontraport')}>Ontraport</button>
                <button onClick={() => setActiveTab('AtoAgent')} className={tabClass(activeTab === 'AtoAgent')}>ATO Agent</button>
              </nav>
            </div>
          </div>

          {!data ? null : (
            <>
              {activeTab === 'SendGrid' ? (
                <div className="overflow-hidden rounded-lg bg-white shadow">
                  <div className="px-4 py-5 sm:p-6">
                    <h3 className="text-lg font-medium leading-6 text-gray-900 mb-4">SendGrid Email Settings</h3>
                    <form onSubmit={onSendGrid}>
                      <div className="space-y-4">
                        <div>
                          <label className="block text-sm font-medium text-gray-700">API Key</label>
                          <input className={inputCls} value={sg.apiKey} onChange={(e) => setSg({ ...sg, apiKey: e.target.value })} />
                        </div>
                        <div>
                          <label className="block text-sm font-medium text-gray-700">From Email</label>
                          <input className={inputCls} value={sg.fromEmail} onChange={(e) => setSg({ ...sg, fromEmail: e.target.value })} />
                        </div>
                        <div>
                          <label className="block text-sm font-medium text-gray-700">From Name</label>
                          <input className={inputCls} value={sg.fromName} onChange={(e) => setSg({ ...sg, fromName: e.target.value })} />
                        </div>
                        <div>
                          <button type="submit" className="inline-flex justify-center rounded-md bg-blue-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-500 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600">
                            Save SendGrid Settings
                          </button>
                        </div>
                      </div>
                    </form>
                  </div>
                </div>
              ) : null}

              {activeTab === 'Stripe' ? (
                <div className="overflow-hidden rounded-lg bg-white shadow">
                  <div className="px-4 py-5 sm:p-6">
                    <h3 className="text-lg font-medium leading-6 text-gray-900 mb-4">Stripe Payment Settings</h3>
                    <form onSubmit={onStripe}>
                      <div className="space-y-4">
                        <div>
                          <label className="block text-sm font-medium text-gray-700">Secret Key</label>
                          <input type="password" className={inputCls} value={stripe.secretKey} onChange={(e) => setStripe({ ...stripe, secretKey: e.target.value })} />
                        </div>
                        <div>
                          <label className="block text-sm font-medium text-gray-700">Publishable Key</label>
                          <input className={inputCls} value={stripe.publishableKey} onChange={(e) => setStripe({ ...stripe, publishableKey: e.target.value })} />
                        </div>
                        <div>
                          <button type="submit" className="inline-flex justify-center rounded-md bg-blue-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-500 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600">
                            Save Stripe Settings
                          </button>
                        </div>
                      </div>
                    </form>
                  </div>
                </div>
              ) : null}

              {activeTab === 'Pricing' ? (
                <div className="overflow-hidden rounded-lg bg-white shadow">
                  <div className="px-4 py-5 sm:p-6">
                    <h3 className="text-lg font-medium leading-6 text-gray-900 mb-4">Renewal Pricing</h3>
                    <p className="text-sm text-gray-500 mb-4">Set the prices customers will pay for business name renewals</p>
                    <form onSubmit={onPricing}>
                      <div className="space-y-4">
                        <div>
                          <label className="block text-sm font-medium text-gray-700">1 Year Renewal Price</label>
                          <div className="relative mt-1 rounded-md shadow-sm">
                            <div className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3">
                              <span className="text-gray-500 sm:text-sm">$</span>
                            </div>
                            <input type="number" step="0.01" value={pricing.oneYearFee} onChange={(e) => setPricing({ ...pricing, oneYearFee: Number(e.target.value) })} className="block w-full rounded-md border-gray-300 pl-7 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm px-3 py-2 border" />
                          </div>
                          <p className="mt-1 text-xs text-gray-500">Total price charged to customer for 1 year renewal</p>
                        </div>
                        <div>
                          <label className="block text-sm font-medium text-gray-700">3 Year Renewal Price</label>
                          <div className="relative mt-1 rounded-md shadow-sm">
                            <div className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3">
                              <span className="text-gray-500 sm:text-sm">$</span>
                            </div>
                            <input type="number" step="0.01" value={pricing.threeYearFee} onChange={(e) => setPricing({ ...pricing, threeYearFee: Number(e.target.value) })} className="block w-full rounded-md border-gray-300 pl-7 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm px-3 py-2 border" />
                          </div>
                          <p className="mt-1 text-xs text-gray-500">Total price charged to customer for 3 year renewal</p>
                        </div>
                        <div>
                          <button type="submit" className="inline-flex justify-center rounded-md bg-blue-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-500 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600">
                            Save Pricing Settings
                          </button>
                        </div>
                      </div>
                    </form>
                  </div>
                </div>
              ) : null}

              {activeTab === 'Asic' ? (
                <div className="overflow-hidden rounded-lg bg-white shadow">
                  <div className="px-4 py-5 sm:p-6">
                    <h3 className="text-lg font-medium leading-6 text-gray-900 mb-4">ASIC Settings</h3>
                    <form onSubmit={onAsic}>
                      <div className="space-y-4">
                        <div>
                          <label className="block text-sm font-medium text-gray-700">Email</label>
                          <input type="email" className={inputCls} value={asic.email} onChange={(e) => setAsic({ ...asic, email: e.target.value })} />
                          <p className="mt-1 text-xs text-gray-500">Email address used for ASIC renewal applications</p>
                        </div>
                        <div>
                          <label className="block text-sm font-medium text-gray-700">Card Number</label>
                          <input className={inputCls} value={asic.cardNumber} onChange={(e) => setAsic({ ...asic, cardNumber: e.target.value })} />
                        </div>
                        <div>
                          <label className="block text-sm font-medium text-gray-700">Cardholder Name</label>
                          <input className={inputCls} value={asic.cardholderName} onChange={(e) => setAsic({ ...asic, cardholderName: e.target.value })} />
                        </div>
                        <div className="grid grid-cols-2 gap-4">
                          <div>
                            <label className="block text-sm font-medium text-gray-700">Expiry Month</label>
                            <input className={inputCls} value={asic.expiryMonth} onChange={(e) => setAsic({ ...asic, expiryMonth: e.target.value })} />
                          </div>
                          <div>
                            <label className="block text-sm font-medium text-gray-700">Expiry Year</label>
                            <input className={inputCls} value={asic.expiryYear} onChange={(e) => setAsic({ ...asic, expiryYear: e.target.value })} />
                          </div>
                        </div>
                        <div>
                          <label className="block text-sm font-medium text-gray-700">CVC</label>
                          <input type="password" className={inputCls} value={asic.cvc} onChange={(e) => setAsic({ ...asic, cvc: e.target.value })} />
                        </div>
                        <div>
                          <button type="submit" className="inline-flex justify-center rounded-md bg-blue-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-500 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600">
                            Save ASIC Settings
                          </button>
                        </div>
                      </div>
                    </form>
                  </div>
                </div>
              ) : null}

              {activeTab === 'Ontraport' ? (
                <div className="overflow-hidden rounded-lg bg-white shadow">
                  <div className="px-4 py-5 sm:p-6">
                    <h3 className="text-lg font-medium leading-6 text-gray-900 mb-4">Ontraport Settings</h3>
                    <form onSubmit={onOntraport}>
                      <div className="space-y-4">
                        <div>
                          <label className="block text-sm font-medium text-gray-700">API App ID</label>
                          <input className={inputCls} value={ontraport.apiAppId} onChange={(e) => setOntraport({ ...ontraport, apiAppId: e.target.value })} />
                        </div>
                        <div>
                          <label className="block text-sm font-medium text-gray-700">API Key</label>
                          <input className={inputCls} value={ontraport.apiKey} onChange={(e) => setOntraport({ ...ontraport, apiKey: e.target.value })} />
                        </div>
                        <div>
                          <label className="block text-sm font-medium text-gray-700">Conversation ID</label>
                          <input className={inputCls} value={ontraport.conversationId} onChange={(e) => setOntraport({ ...ontraport, conversationId: e.target.value })} />
                          <p className="mt-1 text-xs text-gray-500">The Ontraport conversation ID for retrieving SMS messages</p>
                        </div>
                        <div>
                          <button type="submit" className="inline-flex justify-center rounded-md bg-blue-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-500 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600">
                            Save Ontraport Settings
                          </button>
                        </div>
                      </div>
                    </form>
                  </div>
                </div>
              ) : null}

              {activeTab === 'AtoAgent' ? (
                <div className="overflow-hidden rounded-lg bg-white shadow">
                  <div className="px-4 py-5 sm:p-6">
                    <h3 className="text-lg font-medium leading-6 text-gray-900">Default ATO Agent</h3>
                    <p className="mt-1 mb-5 text-sm text-gray-500">
                      All business-name onboarding jobs are filed under this tax agent. The dropdown below
                      lists agents the live myID session is registered against.
                    </p>

                    <form onSubmit={onAtoAgent}>
                      <div className="space-y-5">
                        {!agentSession.loaded ? (
                          <p className="text-sm text-gray-500">Loading agents from the ATO session…</p>
                        ) : agentSession.error ? (
                          <div className="rounded-md bg-red-50 p-3">
                            <p className="text-sm text-red-800">{agentSession.error}</p>
                            <button type="button" onClick={() => void loadAgents()} className="mt-2 text-sm font-medium text-red-700 underline hover:text-red-900">Retry</button>
                          </div>
                        ) : !agentSession.authenticated ? (
                          <div className="rounded-md bg-amber-50 p-3 text-sm text-amber-800">
                            <div className="font-medium">Ato.Api session is not authenticated ({agentSession.phase || 'unknown'}).</div>
                            <div className="mt-1">Sign in to myID via the Ato.Api host first, then come back to pick a default agent.</div>
                            <button type="button" onClick={() => void loadAgents()} className="mt-2 text-sm font-medium text-amber-700 underline hover:text-amber-900">Refresh</button>
                          </div>
                        ) : (
                          <>
                            <div>
                              <label className="block text-sm font-medium text-gray-700">Default Agent</label>
                              <select
                                className={inputCls}
                                value={atoAgent.defaultAgentAbn}
                                onChange={(e) => {
                                  const abn = e.target.value
                                  const found = agentList.find(a => a.abn === abn)
                                  setAtoAgent({ defaultAgentAbn: abn, defaultAgentName: found?.name ?? '' })
                                }}
                              >
                                <option value="">— Select an agent —</option>
                                {agentList.map(a => (
                                  <option key={a.abn} value={a.abn}>{a.name} · {a.abn}</option>
                                ))}
                              </select>
                              <div className="mt-2 flex items-center justify-between">
                                <p className="text-xs text-gray-500">{agentList.length} agent(s) available in this session.</p>
                                <button type="button" onClick={() => void loadAgents()} className="text-xs font-medium text-blue-600 hover:text-blue-700">Refresh list</button>
                              </div>
                            </div>

                            {atoAgent.defaultAgentAbn ? (
                              <div className="rounded-md border border-gray-200 bg-gray-50 px-3 py-2 text-xs text-gray-600">
                                <div><span className="font-medium text-gray-700">Selected:</span> {atoAgent.defaultAgentName}</div>
                                <div className="font-mono">ABN {atoAgent.defaultAgentAbn}</div>
                              </div>
                            ) : null}

                            <div>
                              <button type="submit" disabled={!atoAgent.defaultAgentAbn} className="inline-flex justify-center rounded-md bg-blue-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-500 disabled:opacity-50 disabled:cursor-not-allowed focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600">
                                Save ATO Agent
                              </button>
                            </div>
                          </>
                        )}
                      </div>
                    </form>
                  </div>
                </div>
              ) : null}
            </>
          )}
        </div>
      </main>
    </>
  )
}
