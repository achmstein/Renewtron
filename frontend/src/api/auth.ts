import { apiFetch } from './http'

// Admin sign-in.
export interface MeResponse { email: string; name: string }

export const authApi = {
  me: () => apiFetch<MeResponse>('/api/me'),
  login: (email: string, password: string) =>
    apiFetch<unknown>('/api/login?useCookies=true', { method: 'POST', body: JSON.stringify({ email, password }) }),
  register: (email: string, password: string) =>
    apiFetch<void>('/api/register', { method: 'POST', body: JSON.stringify({ email, password }) }),
  logout: () => apiFetch<void>('/api/logout', { method: 'POST' }),
}
