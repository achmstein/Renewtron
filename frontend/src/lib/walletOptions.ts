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
    // Stripe's card box is an iframe on Stripe's site: it loads our Geist file by full address
    // (nginx allows that with a CORS header on /fonts/).
    fonts: [{
      family: 'Geist',
      src: `url(${window.location.origin}/fonts/geist-latin-wght-normal.woff2)`,
      weight: '400',
      style: 'normal',
      display: 'swap',
    }],
  }
}
