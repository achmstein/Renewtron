import type { StripeElementsOptionsMode } from '@stripe/stripe-js'

/**
 * Options for the <Elements> around a payment form that also offers Apple Pay / Google Pay. The
 * wallets show the amount in their sheet, so it has to be the real total (in cents). "manual" lets
 * the page make a payment method from the wallet and send it to our server, as the card form does.
 */
export function walletElementsOptions(totalDollars: number): StripeElementsOptionsMode {
  return {
    mode: 'payment',
    amount: Math.round(totalDollars * 100),
    currency: 'aud',
    paymentMethodCreation: 'manual',
    fonts: [{ cssSrc: 'https://fonts.googleapis.com/css2?family=Geist:wght@400;500&display=swap' }],
  }
}
