import { useState } from 'react'
import { ExpressCheckoutElement, useElements, useStripe } from '@stripe/react-stripe-js'
import type { StripeExpressCheckoutElementConfirmEvent } from '@stripe/stripe-js'

/** What the wallet hands back: a Stripe payment method, the same thing the card form makes. */
export interface WalletPayment {
  paymentMethodId: string
  /** "Apple Pay" or "Google Pay", for tracking and messages. */
  method: string
  /** The cardholder's name from the wallet, when it gave one. */
  name: string | null
}

const LABEL: Record<string, string> = { apple_pay: 'Apple Pay', google_pay: 'Google Pay' }

/**
 * Apple Pay and Google Pay buttons, above the card form. Stripe shows a button only where it works:
 * Apple Pay in Safari with a card in Wallet, Google Pay in Chrome/Android with a card saved. On any
 * other device nothing shows at all, not even the "or pay by card" line.
 *
 * Needs the page's domain registered with Stripe (Payment method domains) and HTTPS.
 */
export default function WalletButtons({ disabled, onPayment, onError }: {
  disabled?: boolean
  onPayment: (payment: WalletPayment) => void
  onError: (message: string) => void
}) {
  const stripe = useStripe()
  const elements = useElements()
  const [available, setAvailable] = useState(false)

  const onConfirm = async (event: StripeExpressCheckoutElementConfirmEvent) => {
    if (!stripe || !elements) return
    const method = LABEL[event.expressPaymentType] ?? 'Wallet'
    const submitted = await elements.submit()
    if (submitted.error) {
      event.paymentFailed({ reason: 'fail' })
      onError(submitted.error.message ?? `${method} didn’t go through.`)
      return
    }
    const name = event.billingDetails?.name?.trim() || null
    const created = await stripe.createPaymentMethod({
      elements,
      params: name ? { billing_details: { name } } : {},
    })
    if (created.error) {
      event.paymentFailed({ reason: 'fail' })
      onError(created.error.message ?? `${method} didn’t go through.`)
      return
    }
    // The wallet sheet closes here; the page shows its own progress from now on.
    onPayment({ paymentMethodId: created.paymentMethod.id, method, name })
  }

  return (
    // Kept mounted but out of sight until Stripe says a wallet is ready on this device.
    <div className={available ? 'mb-6' : 'pointer-events-none invisible h-0 overflow-hidden'} aria-hidden={!available}>
      <div className={disabled ? 'pointer-events-none opacity-50' : undefined}>
        <ExpressCheckoutElement
          options={{
            paymentMethods: { applePay: 'auto', googlePay: 'auto', link: 'never', paypal: 'never', amazonPay: 'never' },
            buttonType: { applePay: 'plain', googlePay: 'plain' },
            buttonHeight: 48,
            layout: { maxColumns: 1, overflow: 'never' },
          }}
          onReady={({ availablePaymentMethods }) =>
            setAvailable(!!availablePaymentMethods && (availablePaymentMethods.applePay || availablePaymentMethods.googlePay))}
          onConfirm={(e) => void onConfirm(e)}
        />
      </div>
      {available ? (
        <div className="mt-6 flex items-center gap-3 text-xs text-gray-500">
          <span className="h-px flex-1 bg-gray-200" />
          or pay by card
          <span className="h-px flex-1 bg-gray-200" />
        </div>
      ) : null}
    </div>
  )
}
