import { api } from '../../api/client'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { AsicPaymentForm } from './model'
import { Field, SettingsCard, type CardHeaderProps } from './ui'
import { useSave, type FormProps } from './useSave'

/** How Renewtron pays ASIC: card or BPAY, the card itself, ASIC's fees and paying early. */
export function AsicPaymentCard({ card, value: asic, onChange: setAsic, hasCvc, onCvcStored }: {
  card: CardHeaderProps
  /** A CVC is stored on the server (it never comes back). */
  hasCvc: boolean
  onCvcStored: () => void
} & FormProps<AsicPaymentForm>) {
  const save = useSave()
  const onAsic = save('ASIC', () => api.admin.updateAsic(asic).then(() => { if (asic.cvc) onCvcStored() }))

  return (
    <SettingsCard {...card} onSubmit={onAsic}>
      <Field
        label="Pay ASIC by"
        hint={asic.paymentMethod === 'Bpay' ? 'Lodged to pay later; pay the BPAY bill, then mark it paid.' : 'Paid at ASIC straight away with the card below.'}
        tip={asic.paymentMethod === 'Bpay'
          ? 'Each renewal is lodged with ASIC to pay later. Its BPAY biller code and reference are saved on the renewal — pay them at the bank, then mark it paid.'
          : 'Each renewal is paid at ASIC straight away with the card below (needs the SMS code).'}
      >
        <Select value={asic.paymentMethod} onValueChange={(v) => setAsic({ ...asic, paymentMethod: v as 'Card' | 'Bpay' })}>
          <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="Card">Credit card now</SelectItem>
            <SelectItem value="Bpay">Pay later by BPAY</SelectItem>
          </SelectContent>
        </Select>
      </Field>
      {asic.paymentMethod === 'Card' ? (
        <Field
          label="Switch to BPAY after"
          hint={asic.bpayAfterCardFailures > 0 ? 'Then the renewal is lodged to pay later by BPAY.' : 'Renewals are only ever paid by card.'}
          tip={asic.bpayAfterCardFailures > 0
            ? `After ${asic.bpayAfterCardFailures} failed card ${asic.bpayAfterCardFailures === 1 ? 'attempt' : 'attempts'} on a renewal (declined, or no SMS code), it's lodged to pay later by BPAY instead. Card retries are an hour apart.`
            : undefined}
        >
          <Select value={String(asic.bpayAfterCardFailures)} onValueChange={(v) => setAsic({ ...asic, bpayAfterCardFailures: Number(v) })}>
            <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="0">Never</SelectItem>
              {[1, 2, 3, 4, 5].map((n) => <SelectItem key={n} value={String(n)}>{n} failed card {n === 1 ? 'attempt' : 'attempts'}</SelectItem>)}
            </SelectContent>
          </Select>
        </Field>
      ) : null}
      <Field
        label="Names not due yet"
        hint={asic.allowPayEarly ? 'Customers can pay now; Renewtron lodges it when ASIC opens the renewal.' : 'The renewal site stops at “not due for renewal yet”.'}
        tip="ASIC only takes a renewal in the 29 days before the name is due. With paying early on, the renewal site lists the names anyway and takes payment; each morning Renewtron asks ASIC again and lodges the renewal as soon as ASIC lists the name. The customer is never told the name isn't due."
      >
        <Select value={asic.allowPayEarly ? 'yes' : 'no'} onValueChange={(v) => setAsic({ ...asic, allowPayEarly: v === 'yes' })}>
          <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="yes">Let customers pay early</SelectItem>
            <SelectItem value="no">Stop at “not due yet”</SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field label="Email">
        <Input type="email" value={asic.email} onChange={(e) => setAsic({ ...asic, email: e.target.value })} />
      </Field>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="Card number">
          <Input className="font-mono tabular-nums" value={asic.cardNumber} onChange={(e) => setAsic({ ...asic, cardNumber: e.target.value })} />
        </Field>
        <Field label="Cardholder name">
          <Input value={asic.cardholderName} onChange={(e) => setAsic({ ...asic, cardholderName: e.target.value })} />
        </Field>
      </div>
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
        <Field label="Expiry month">
          <Input className="font-mono tabular-nums" value={asic.expiryMonth} onChange={(e) => setAsic({ ...asic, expiryMonth: e.target.value })} />
        </Field>
        <Field label="Expiry year">
          <Input className="font-mono tabular-nums" value={asic.expiryYear} onChange={(e) => setAsic({ ...asic, expiryYear: e.target.value })} />
        </Field>
        <Field label="CVC" hint={hasCvc ? 'Stored. Leave blank to keep it.' : undefined}>
          <Input type="password" className="font-mono tabular-nums" value={asic.cvc} onChange={(e) => setAsic({ ...asic, cvc: e.target.value })} placeholder={hasCvc ? '•••' : ''} />
        </Field>
      </div>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field
          label="ASIC fee, 1 year (A$)"
          hint="Update when ASIC’s fees change (usually 1 July)."
          tip="What ASIC charges for a 1-year renewal. BPAY bills are paid this amount through Airwallex. Update it when ASIC's fees change (usually 1 July)."
        >
          <Input type="number" min="0" step="0.01" className="font-mono tabular-nums" value={asic.oneYearFee || ''} onChange={(e) => setAsic({ ...asic, oneYearFee: Number(e.target.value) || 0 })} />
        </Field>
        <Field label="ASIC fee, 3 years (A$)" tip="What ASIC charges for a 3-year renewal.">
          <Input type="number" min="0" step="0.01" className="font-mono tabular-nums" value={asic.threeYearFee || ''} onChange={(e) => setAsic({ ...asic, threeYearFee: Number(e.target.value) || 0 })} />
        </Field>
      </div>
    </SettingsCard>
  )
}
