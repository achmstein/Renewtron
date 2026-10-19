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
  rememberDetails({ fullName: details.fullName.trim() })
  return result.leadId
}
