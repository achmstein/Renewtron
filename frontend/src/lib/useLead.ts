import { useCallback, useEffect, useState } from 'react'
import { api, type LeadDto } from '../api/client'

const POLL_MS = 3000
const WAIT_LIMIT_MS = 3 * 60 * 1000

/**
 * Loads a lead and, while its business names are still waiting on ASIC's confirmation
 * (`verified: false`), keeps re-fetching it so account numbers fill in and names ASIC
 * won't renew drop out without a reload.
 */
export function useLead(leadId: string | undefined, onMissing: () => void) {
  const [lead, setLead] = useState<LeadDto | null>(null)

  useEffect(() => {
    if (!leadId) return
    void api.getLead(leadId).then(setLead).catch(onMissing)
    // onMissing is a navigation callback; only the lead id should restart the load.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leadId])

  const pending = lead !== null && !lead.verified
  useEffect(() => {
    if (!leadId || !pending) return
    const started = Date.now()
    const timer = window.setInterval(() => {
      if (Date.now() - started > WAIT_LIMIT_MS) { window.clearInterval(timer); return }
      void api.getLead(leadId).then(setLead).catch(() => {})
    }, POLL_MS)
    return () => window.clearInterval(timer)
  }, [leadId, pending])

  /** Resolves with the confirmed lead; rejects if ASIC hasn't answered within a few minutes. */
  const waitUntilVerified = useCallback(async (): Promise<LeadDto> => {
    if (!leadId) throw new Error('Missing lead.')
    const started = Date.now()
    for (;;) {
      const current = await api.getLead(leadId)
      setLead(current)
      if (current.verified) return current
      if (Date.now() - started > WAIT_LIMIT_MS)
        throw new Error("ASIC is taking longer than usual to respond. You haven't been charged — please try again in a few minutes.")
      await new Promise((r) => setTimeout(r, POLL_MS))
    }
  }, [leadId])

  return { lead, waitUntilVerified }
}
