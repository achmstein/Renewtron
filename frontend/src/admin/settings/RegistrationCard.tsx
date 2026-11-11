import { api, type RegistrationSettings } from '../../api/client'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Field, MoneyInput, SettingsCard, type CardHeaderProps } from './ui'
import { useSave, type FormProps } from './useSave'

/** The public /register page: open or closed, lodging or dry run, and its prices. */
export function RegistrationCard({ card, value: registration, onChange: setRegistration }: { card: CardHeaderProps } & FormProps<RegistrationSettings>) {
  const save = useSave()
  const onRegistration = save('New business names', () => api.admin.updateRegistrationSettings(registration))
  return (
    <SettingsCard {...card} onSubmit={onRegistration}>
      <Field label="Register page" hint={registration.enabled ? 'Open: /register takes applications and payments.' : 'Closed: /register says the service isn’t open yet.'}>
        <Select value={registration.enabled ? 'on' : 'off'} onValueChange={(v) => setRegistration({ ...registration, enabled: v === 'on' })}>
          <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="on">Open</SelectItem>
            <SelectItem value="off">Closed</SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field label="Lodge with ASIC" hint={registration.lodge ? 'Paid applications are lodged with ASIC.' : 'Dry run: filled in at ASIC up to Review, then cancelled. Nothing is lodged.'}>
        <Select value={registration.lodge ? 'on' : 'off'} onValueChange={(v) => setRegistration({ ...registration, lodge: v === 'on' })}>
          <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="on">Lodge</SelectItem>
            <SelectItem value="off">Dry run only</SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="1-year price" hint="What the customer pays, ASIC’s fee included.">
          <MoneyInput value={registration.oneYearPrice} onChange={(n) => setRegistration({ ...registration, oneYearPrice: n })} />
        </Field>
        <Field label="3-year price">
          <MoneyInput value={registration.threeYearPrice} onChange={(n) => setRegistration({ ...registration, threeYearPrice: n })} />
        </Field>
      </div>
      <Field label="Names ASIC refers for a decision" hint={registration.allowManualDecision ? 'Lodged anyway; ASIC decides later and may refuse.' : 'Stopped before lodging, and the customer is refunded.'}
        tip="ASIC refers names it doesn’t know (made-up words, restricted words) for a manual decision. It lodges them without asking for payment and may refuse them weeks later.">
        <Select value={registration.allowManualDecision ? 'on' : 'off'} onValueChange={(v) => setRegistration({ ...registration, allowManualDecision: v === 'on' })}>
          <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="off">Stop and refund</SelectItem>
            <SelectItem value="on">Lodge anyway</SelectItem>
          </SelectContent>
        </Select>
      </Field>
    </SettingsCard>
  )
}
