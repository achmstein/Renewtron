import { cn } from '@/lib/utils'

/** RENEW / TRON, the app's name as a wordmark. `dark` for dark backgrounds (the admin sidebar). */
export function Wordmark({ tone = 'light', className }: { tone?: 'light' | 'dark'; className?: string }) {
  return (
    <span className={cn('inline-flex items-baseline font-display font-bold leading-none tracking-wide', tone === 'dark' ? 'text-white' : 'text-zinc-900', className)}>
      <span>RENEW</span>
      <span className={cn('mx-1.5 font-normal', tone === 'dark' ? 'text-zinc-600' : 'text-zinc-300')}>/</span>
      <span className={tone === 'dark' ? 'text-brand-400' : 'text-brand-500'}>TRON</span>
    </span>
  )
}
