/*
 * Sample values for skeletons (components/skeletonizer.tsx). Never shown as text: they only give
 * the bones their length, so they vary like real data does. Pick by row index: sample.name(i).
 */
const NAMES = ['Sarah Mitchell', 'James Okafor', 'Priya Raman', 'Tom Nguyen', 'Emily Clarke-Watson', 'Daniel Ho', 'Grace Lee', 'Michael Fitzgerald']
const BUSINESSES = ['MITCHELL CONSULTING', 'OKAFOR PLUMBING SERVICES', 'RAMAN DESIGN', 'NGUYEN BROS ELECTRICAL', 'CLARKE & CO', 'HO TRADING', 'LEE BEAUTY STUDIO', 'FITZGERALD LANDSCAPING']

const pick = <T,>(list: T[], i: number) => list[((i % list.length) + list.length) % list.length]

/** A date i days before now, as ISO. */
const daysAgo = (i: number) => new Date(Date.now() - i * 86_400_000).toISOString()

export const sample = {
  id: (i: number) => `00000000-0000-0000-0000-${String(i).padStart(12, '0')}`,
  name: (i: number) => pick(NAMES, i),
  business: (i: number) => pick(BUSINESSES, i),
  email: (i: number) => `${pick(NAMES, i).split(' ')[0].toLowerCase()}@example.com.au`,
  mobile: () => '0412 345 678',
  abn: (i: number) => `5100${String(1000000 + i * 7919).slice(0, 7)}`,
  money: (i: number) => [99, 299, 87, 598, 149][i % 5],
  daysAgo,
  /** A date i days from now, as ISO. */
  daysAhead: (i: number) => daysAgo(-i),
  text: (i: number) => pick(['Card declined by the bank', 'Waiting on ASIC to confirm', 'Renewed for 3 years', 'Key letter not found in the inbox', 'Paid by BPAY'], i),
}
