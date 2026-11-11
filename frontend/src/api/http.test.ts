import { afterEach, describe, expect, it, vi } from 'vitest'
import { apiFetch } from './http'

const fetchMock = vi.fn<typeof fetch>()
vi.stubGlobal('fetch', fetchMock)
afterEach(() => fetchMock.mockReset())

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('apiFetch', () => {
  it('returns the parsed JSON body', async () => {
    fetchMock.mockResolvedValue(json({ oneYearFee: 99 }))
    await expect(apiFetch('/api/pricing')).resolves.toEqual({ oneYearFee: 99 })
  })

  it('sends cookies and a JSON content type, keeping the caller’s method and body', async () => {
    fetchMock.mockResolvedValue(json({}))
    await apiFetch('/api/leads', { method: 'POST', body: '{"abn":"1"}' })
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/leads')
    expect(init).toMatchObject({ credentials: 'include', method: 'POST', body: '{"abn":"1"}', headers: { 'Content-Type': 'application/json' } })
  })

  it('leaves the content type to the browser for form uploads', async () => {
    fetchMock.mockResolvedValue(json({}))
    await apiFetch('/api/upload', { method: 'POST', body: new FormData() })
    expect(fetchMock.mock.calls[0][1]?.headers).toEqual({})
  })

  it('returns undefined for 204 and for non-JSON answers', async () => {
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))
    await expect(apiFetch('/api/x')).resolves.toBeUndefined()
    fetchMock.mockResolvedValueOnce(new Response('ok', { status: 200, headers: { 'Content-Type': 'text/plain' } }))
    await expect(apiFetch('/api/x')).resolves.toBeUndefined()
  })

  describe('errors', () => {
    it('uses the body’s error first', async () => {
      fetchMock.mockResolvedValue(json({ error: 'Card declined', detail: 'd', title: 't' }, 400))
      await expect(apiFetch('/api/x')).rejects.toThrow('Card declined')
    })

    it('falls back to detail, then title (problem details)', async () => {
      fetchMock.mockResolvedValueOnce(json({ detail: 'ABN is invalid', title: 'Bad Request' }, 400))
      await expect(apiFetch('/api/x')).rejects.toThrow('ABN is invalid')
      fetchMock.mockResolvedValueOnce(json({ title: 'Not Found' }, 404))
      await expect(apiFetch('/api/x')).rejects.toThrow('Not Found')
    })

    it('says the status when the JSON has no message', async () => {
      fetchMock.mockResolvedValue(json({ something: 'else' }, 500))
      await expect(apiFetch('/api/x')).rejects.toThrow('Request failed: 500')
    })

    it('uses a plain-text body as the message', async () => {
      fetchMock.mockResolvedValue(new Response('Service unavailable', { status: 503 }))
      await expect(apiFetch('/api/x')).rejects.toThrow('Service unavailable')
    })

    it('says the status when the body is empty', async () => {
      fetchMock.mockResolvedValue(new Response('', { status: 502 }))
      await expect(apiFetch('/api/x')).rejects.toThrow('Request failed: 502')
    })
  })

  it('keeps the JSON content type when the caller adds headers of its own', async () => {
    fetchMock.mockResolvedValueOnce(json({ ok: true }))
    await apiFetch('/api/x', { method: 'POST', body: '{}', headers: { 'X-Test': '1' } })
    expect(fetchMock.mock.calls[0][1]?.headers).toEqual({ 'Content-Type': 'application/json', 'X-Test': '1' })
  })
})
