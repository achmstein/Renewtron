import { apiFetch } from './http'

// A record's "What happened" story.
/** The kinds of record a journey links (see JourneyService on the server). */
export type JourneyRecord = 'lead' | 'search' | 'run' | 'order' | 'sale' | 'email'
/** One thing that happened, as a sentence, on the record it happened to. */
export interface JourneyEvent {
  at: string
  text: string
  detail: string | null
  meaning: 'action' | 'waiting' | 'working' | 'done' | 'quiet'
  record: JourneyRecord
  recordId: string
}

export const journeyAdminApi = {
  /** Everything that happened around a record, oldest first: the same story on every record page of the journey. */
  journey: (kind: Exclude<JourneyRecord, 'email'>, id: string) =>
    apiFetch<{ events: JourneyEvent[] }>(`/api/admin/journey/${kind}/${id}`),
}
