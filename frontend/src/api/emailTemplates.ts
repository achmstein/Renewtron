import { apiFetch } from './client'

/** Emails → Templates (EmailTemplatesModule on the server). */

export interface TemplateSummary {
  key: string
  name: string
  description: string
  enabled: boolean
  /** 0 = the wording in code. */
  version: number
  updatedAt: string | null
  updatedBy: string | null
  lastSentAt: string | null
}

export interface TemplateVariable {
  name: string
  label: string
  description: string
  kind: 'text' | 'flag' | 'list'
  sample: unknown
}

export interface TemplateContent {
  subject: string
  heading: string
  body: string
  version: number
}

export interface TemplateDetail {
  key: string
  name: string
  description: string
  isLayout: boolean
  previewSource: 'renewal' | 'lead' | 'registration' | null
  enabled: boolean
  variables: TemplateVariable[]
  current: TemplateContent
  default: TemplateContent
}

export interface TemplateProblem { field: 'subject' | 'heading' | 'body' | 'layout' | string; message: string }

export interface TemplatePreview { subject: string; html: string; text: string; problems: TemplateProblem[] }

export interface Draft { subject: string; heading: string; body: string }

export interface SampleRecord { id: string; title: string; detail: string | null; at: string }

export interface TemplateVersion {
  version: number
  source: 'edit' | 'default' | 'rollback'
  restoredFrom: number | null
  note: string | null
  createdAt: string
  createdBy: string | null
  /** Emails sent with this version. */
  sent: number
}

const base = (key: string) => `/api/admin/email-templates/${encodeURIComponent(key)}`
const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) })

export const templatesApi = {
  list: () => apiFetch<{ templates: TemplateSummary[]; layout: TemplateSummary }>('/api/admin/email-templates/'),
  get: (key: string) => apiFetch<TemplateDetail>(base(key)),
  preview: (key: string, draft: Draft, sampleId: string | null) =>
    apiFetch<TemplatePreview>(`${base(key)}/preview`, json('POST', { ...draft, sampleId })),
  save: (key: string, draft: Draft, note: string) =>
    apiFetch<{ version: number; message: string }>(base(key), json('PUT', { ...draft, note })),
  reset: (key: string) => apiFetch<{ version: number; message: string }>(`${base(key)}/reset`, json('POST')),
  setEnabled: (key: string, enabled: boolean) =>
    apiFetch<{ enabled: boolean; message: string }>(`${base(key)}/enabled`, json('PUT', { enabled })),
  test: (key: string, draft: Draft, sampleId: string | null, to?: string) =>
    apiFetch<{ message: string }>(`${base(key)}/test`, json('POST', { ...draft, sampleId, to })),
  samples: (key: string, search: string) =>
    apiFetch<SampleRecord[]>(`${base(key)}/samples?${new URLSearchParams(search ? { search } : {})}`),
  versions: (key: string) => apiFetch<{ versions: TemplateVersion[]; sentWithDefault: number }>(`${base(key)}/versions`),
  version: (key: string, version: number) => apiFetch<TemplateContent>(`${base(key)}/versions/${version}`),
  restore: (key: string, version: number) =>
    apiFetch<{ version: number; message: string }>(`${base(key)}/versions/${version}/restore`, json('POST')),
}
