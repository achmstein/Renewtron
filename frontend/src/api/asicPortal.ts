import { apiFetch } from './http'

// Linking names to our ASIC Business Portal account.
export type AsicPortalLinkStatus = 'Queued' | 'Running' | 'Done' | 'WrongKey' | 'NeedsLook' | 'Failed'
export type AsicDeclareAs = 'Holder' | 'OnBehalfOfHolder'

/** One "link to our ASIC account" run: link, business email, holder email, representatives. */
export interface AsicPortalLinkDto {
  id: string
  clientId: string | null
  clientName: string | null
  clientBusinessNameId: string
  businessName: string
  abn: string
  dateOfBirth: string
  dateOfBirthSource: string
  ontraportContactId: string | null
  declareAs: AsicDeclareAs
  status: AsicPortalLinkStatus
  linkOutcome: string | null
  emailOutcome: string | null
  emailReference: string | null
  holderOutcome: string | null
  holderType: string | null
  holderReference: string | null
  repsOutcome: string | null
  repsRemoved: string[]
  repsReferences: string[]
  failedAtStep: string | null
  error: string | null
  ontraportResult: string | null
  requestedBy: string | null
  attemptCount: number
  createdAt: string
  startedAt: string | null
  finishedAt: string | null
}

export const asicPortalAdminApi = {
  linkToAsicPortal: (nameId: string, declareAs: AsicDeclareAs) => apiFetch<{ id: string; message: string }>(`/api/admin/asic-portal/business-names/${nameId}/link`, { method: 'POST', body: JSON.stringify({ declareAs }) }),
  asicPortalLink: (id: string) => apiFetch<AsicPortalLinkDto>(`/api/admin/asic-portal/${id}`),
  asicPortalLinks: (params: { status?: string; clientId?: string } = {}) => apiFetch<AsicPortalLinkDto[]>(`/api/admin/asic-portal/?${new URLSearchParams(Object.entries(params).filter(([, v]) => v) as [string, string][])}`),
  retryAsicPortalLink: (id: string) => apiFetch<{ message: string }>(`/api/admin/asic-portal/${id}/retry`, { method: 'POST' }),
}
