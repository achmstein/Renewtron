import { api } from '../api/client'
import { rememberDetails, type Prefill } from './prefill'
import { getVisitorId } from './tracking'

export interface LeadDetails {
  abn: string
  fullName: string
  email: string
  mobileNumber: string
  dateOfBirth: string
}

/** Creates the lead for the check step — from the details form, or straight from a portal link. */
export async function createLead(details: LeadDetails, prefill: Prefill): Promise<string> {
  const result = await api.createLead({
    abn: details.abn.replace(/\s+/g, ''),
    fullName: details.fullName.trim(),
    email: details.email.trim().toLowerCase(),
    mobileNumber: details.mobileNumber.replace(/[\s-]/g, ''),
    dateOfBirth: details.dateOfBirth,
    source: prefill.source || (prefill.contactId ? 'ontraport' : undefined),
    ontraportContactId: prefill.contactId || undefined,
    visitorId: getVisitorId(),
  })
  // Kept for this tab so "Edit" and Back show the form filled in: the lead endpoint only returns
  // masked values, so the details page can't refill itself from the server.
  // The ABN isn't kept: the details page takes it from its link, and a stored one would show on the
  // start page as "filled in from your renewal notice".
  rememberDetails({
    fullName: details.fullName.trim(),
    email: details.email.trim().toLowerCase(),
    mobile: details.mobileNumber.replace(/[\s-]/g, ''),
    dob: details.dateOfBirth,
  })
  return result.leadId
}
