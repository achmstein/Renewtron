import { useEffect, useState } from 'react'

export interface PageState<T> {
  items: T[]
  total: number
  skip: number
  take: number
  loading: boolean
  error: string | null
  next: () => void
  prev: () => void
  refresh: () => void
}

export function usePager<T>(loader: (skip: number, take: number) => Promise<{ total: number; items: T[] }>, take = 25): PageState<T> {
  const [skip, setSkip] = useState(0)
  const [items, setItems] = useState<T[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)

  useEffect(() => {
    let active = true
    setLoading(true)
    loader(skip, take)
      .then((r) => {
        if (!active) return
        setItems(r.items)
        setTotal(r.total)
        setError(null)
      })
      .catch((e) => active && setError(e instanceof Error ? e.message : 'Load failed.'))
      .finally(() => active && setLoading(false))
    return () => { active = false }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [skip, take, tick])

  return {
    items, total, skip, take, loading, error,
    next: () => setSkip((s) => Math.min(s + take, Math.max(total - take, 0))),
    prev: () => setSkip((s) => Math.max(0, s - take)),
    refresh: () => setTick((t) => t + 1),
  }
}
