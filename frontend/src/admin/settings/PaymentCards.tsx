import { sileo } from 'sileo'
import { api } from '../../api/client'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { SettingsForms } from './model'
import { Field, MoneyInput, SettingsCard, SwitchField, type CardHeaderProps } from './ui'
import { useSave, type FormProps } from './useSave'

export function StripeCard({ card, value: stripe, onChange: setStripe }: { card: CardHeaderProps } & FormProps<SettingsForms['stripe']>) {
  const save = useSave()
  const onStripe = save('Stripe', () => api.admin.updateStripe(stripe))
  return (
    <SettingsCard {...card} onSubmit={onStripe}>
      <Field label="Secret key">
        <Input type="password" className="font-mono" value={stripe.secretKey} onChange={(e) => setStripe({ ...stripe, secretKey: e.target.value })} />
      </Field>
      <Field label="Publishable key">
        <Input className="font-mono" value={stripe.publishableKey} onChange={(e) => setStripe({ ...stripe, publishableKey: e.target.value })} />
      </Field>
      <Field
        label="Dispute webhook signing secret"
        hint="Starts whsec_. New disputes get Renewtron’s evidence staged."
        tip="Stripe dashboard → Developers → Webhooks: add an endpoint at this site's /api/stripe/webhook for the charge.dispute.created event, then paste its signing secret (whsec_…). New disputes get Renewtron's evidence staged for Smart Disputes."
      >
        <Input type="password" className="font-mono" value={stripe.webhookSecret} onChange={(e) => setStripe({ ...stripe, webhookSecret: e.target.value })} />
      </Field>
    </SettingsCard>
  )
}

export function AirwallexCard({ card, value: airwallex, onChange: setAirwallex }: { card: CardHeaderProps } & FormProps<SettingsForms['airwallex']>) {
  const save = useSave()
  const onAirwallex = save('Airwallex', () => api.admin.updateAirwallex(airwallex))
  const testAirwallex = () => {
    void sileo.promise(api.admin.testAirwallex(), {
      loading: { title: 'Logging in to Airwallex…' },
      success: () => ({ title: 'Airwallex keys work' }),
      error: (e) => ({ title: 'Airwallex login failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  return (
    <SettingsCard
      {...card}
      onSubmit={onAirwallex}
      extra={<Button type="button" variant="outline" onClick={testAirwallex}>Test saved keys</Button>}
    >
      <Field label="Environment" hint={airwallex.useSandbox ? 'Payments are simulated; use sandbox keys.' : 'Payments really leave your Airwallex AUD balance; use live keys.'}>
        <Select value={airwallex.useSandbox ? 'sandbox' : 'live'} onValueChange={(v) => setAirwallex({ ...airwallex, useSandbox: v === 'sandbox' })}>
          <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="sandbox">Sandbox (no money moves)</SelectItem>
            <SelectItem value="live">Live</SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field label="Client ID">
        <Input className="font-mono" value={airwallex.clientId} onChange={(e) => setAirwallex({ ...airwallex, clientId: e.target.value })} />
      </Field>
      <Field label="API key">
        <Input type="password" className="font-mono" value={airwallex.apiKey} onChange={(e) => setAirwallex({ ...airwallex, apiKey: e.target.value })} />
      </Field>
      <SwitchField id="aw-autopay" label="Pay BPAY bills automatically"
        description="Every 15 minutes: renewals and new names lodged to pay by BPAY, at ASIC’s set fee. A bill that fails 3 times waits on BPAY to pay. Live only: never in sandbox. Off: only when someone presses pay."
        checked={airwallex.autoPayBpay !== false} onCheckedChange={(v) => setAirwallex({ ...airwallex, autoPayBpay: v })} />
      <Field label="Daily limit (A$)" hint="The most paid in one day, counting payments made by hand. Bills over it wait for the next day or a person.">
        <Input type="number" min={0} step={50} className="font-mono tabular-nums" value={airwallex.autoPayDailyLimit ?? 3000}
          onChange={(e) => setAirwallex({ ...airwallex, autoPayDailyLimit: Number(e.target.value) })} />
      </Field>
    </SettingsCard>
  )
}

export function PricingCard({ card, value: pricing, onChange: setPricing }: { card: CardHeaderProps } & FormProps<SettingsForms['pricing']>) {
  const save = useSave()
  const onPricing = save('Pricing', () => api.admin.updatePricing(pricing))
  return (
    <SettingsCard {...card} onSubmit={onPricing}>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="1-year renewal price">
          <MoneyInput value={pricing.oneYearFee} onChange={(n) => setPricing({ ...pricing, oneYearFee: n })} />
        </Field>
        <Field label="3-year renewal price">
          <MoneyInput value={pricing.threeYearFee} onChange={(n) => setPricing({ ...pricing, threeYearFee: n })} />
        </Field>
      </div>
    </SettingsCard>
  )
}
