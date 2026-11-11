import { useEffect, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { CircleCheck, CreditCard, Loader2, Search } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../api/client'
import { Field, pageClass, PageHeader } from './_ui'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { cn } from '@/lib/utils'

type SearchResult = { id: string; businessName: string; accountNumber: string; registrationDate: string }

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
    onSuccess: () => { setTimeout(() => navigate('/admin/renewal-orders'), 1800) },
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
    <div className={pageClass()}>
      <PageHeader
        title="Manual renewal"
        subtitle="Renew a name for a client who paid us another way."
      />

      {/* Side by side on wide screens: pick the name on the left, fill in its renewal on the right. */}
      <div className="grid items-start gap-6 lg:grid-cols-2">
        <Step n={1} title="Find the business" subtitle="Look up the ABN at ASIC.">
          <div className="flex gap-2">
            <Input
              type="text"
              value={searchAbn}
              onChange={(e) => setSearchAbn(e.target.value.replace(/\D/g, '').slice(0, 11))}
              onKeyDown={(e) => { if (e.key === 'Enter') search() }}
              placeholder="11-digit ABN"
              aria-label="ABN"
              className="flex-1 font-mono tabular-nums"
              maxLength={11}
            />
            <Button variant="outline" onClick={search} disabled={searchMutation.isPending}>
              {searchMutation.isPending ? <Loader2 className="animate-spin" /> : <Search />}
              Search
            </Button>
          </div>

          {searchResults.length > 0 ? (
            <RadioGroup
              className="mt-5 gap-2"
              aria-label="Business"
              value={selectedResult?.id ?? ''}
              onValueChange={(id) => setSelectedResult(searchResults.find((r) => r.id === id) ?? null)}
            >
              {searchResults.map((r) => (
                <label
                  key={r.id}
                  className={cn(
                    'flex cursor-pointer items-start gap-3 rounded-lg border p-3 transition-colors hover:bg-muted/50',
                    selectedResult?.id === r.id && 'border-ring bg-link/5',
                  )}
                >
                  <RadioGroupItem value={r.id} className="mt-0.5" />
                  <div className="min-w-0 flex-1">
                    <div className="text-sm font-medium">{r.businessName}</div>
                    <div className="text-caption text-muted-foreground">
                      Account <span className="font-mono tabular-nums">{r.accountNumber}</span> · registered {r.registrationDate}
                    </div>
                  </div>
                </label>
              ))}
            </RadioGroup>
          ) : null}
        </Step>

        <Step n={2} title="Renewal details" subtitle={selectedResult ? selectedResult.businessName : 'Pick a business first.'} dimmed={!selectedResult}>
          {selectedResult ? (
            <div className="space-y-5">
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Field label="Renewal years">
                  <Select value={String(renewalYears)} onValueChange={(v) => onYearsChange(Number(v) as 1 | 3)}>
                    <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="1">1 year · ${oneYearDefault}</SelectItem>
                      <SelectItem value="3">3 years · ${threeYearDefault}</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field label="Customer email">
                  <Input type="email" value={customerEmail} onChange={(e) => setCustomerEmail(e.target.value)} placeholder="customer@example.com" />
                </Field>
                <Field label="Mobile number">
                  <Input type="tel" value={mobileNumber} onChange={(e) => setMobileNumber(e.target.value)} placeholder="04XX XXX XXX" className="tabular-nums" />
                </Field>
                <Field label="Date of birth (optional)">
                  <Input type="date" value={dateOfBirth} onChange={(e) => setDateOfBirth(e.target.value)} className="tabular-nums" />
                </Field>
                <Field label="Amount the customer paid">
                  <div className="relative" title="Filled in with our price. Change it if the customer paid a different amount.">
                    <span className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 text-sm text-muted-foreground">$</span>
                    <Input type="number" step="0.01" min="0" value={amount} onChange={(e) => setAmount(Number(e.target.value))} className="pl-7 font-mono tabular-nums" />
                  </div>
                </Field>
              </div>

              <Alert>
                <CreditCard />
                <AlertDescription>ASIC is paid with the company card from Settings.</AlertDescription>
              </Alert>

              <Button className="w-full sm:w-auto" onClick={process} disabled={processMutation.isPending}>
                {processMutation.isPending ? <Loader2 className="animate-spin" /> : <CircleCheck />}
                {processMutation.isPending ? 'Starting…' : 'Start renewal'}
              </Button>
            </div>
          ) : null}
        </Step>
      </div>
    </div>
  )
}

function Step({ n, title, subtitle, dimmed, children }: { n: number; title: string; subtitle?: string; dimmed?: boolean; children: ReactNode }) {
  return (
    <Card className={cn(dimmed && 'opacity-60')}>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <span className="inline-flex size-6 items-center justify-center rounded-full bg-muted text-xs font-medium text-muted-foreground tabular-nums">{n}</span>
          {title}
        </CardTitle>
        {subtitle ? <CardDescription>{subtitle}</CardDescription> : null}
      </CardHeader>
      {dimmed ? null : <CardContent>{children}</CardContent>}
    </Card>
  )
}
