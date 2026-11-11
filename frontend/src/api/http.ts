// The one fetch wrapper every API call goes through, and shared response shapes.
export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const isForm = init?.body instanceof FormData
  // init first, so a caller's own headers add to the JSON content type instead of replacing it.
  const response = await fetch(path, {
    credentials: 'include',
    ...init,
    headers: {
      ...(isForm ? {} : { 'Content-Type': 'application/json' }),
      ...(init?.headers as Record<string, string> | undefined ?? {}),
    },
  })

  if (!response.ok) {
    const text = await response.text().catch(() => '')
    let message = `Request failed: ${response.status}`
    try {
      const parsed = JSON.parse(text)
      message = parsed.error || parsed.detail || parsed.title || message
    } catch {
      if (text) message = text
    }
    throw new Error(message)
  }

  if (response.status === 204) return undefined as T
  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.includes('application/json')) return undefined as T
  return (await response.json()) as T
}

/** Server-computed facet options: value + row count under the other active filters. */
export type Facet = Array<{ value: string; count: number }>
