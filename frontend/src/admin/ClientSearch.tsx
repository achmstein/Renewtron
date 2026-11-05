import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Loader2, Search } from 'lucide-react'
import { api } from '../api/client'
import { useDebouncedValue } from './_components'
import { formatAbn } from './_utils'
import { CommandDialog, CommandEmpty, CommandInput, CommandItem, CommandList } from '@/components/ui/command'

const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform)

/** The sidebar's "Find a client" button; the dialog itself lives once in the layout. */
export function ClientSearchButton({ onClick }: { onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="flex h-9 w-full items-center gap-2 rounded-md border border-sidebar-border bg-sidebar-accent px-3 text-sm text-sidebar-foreground transition hover:text-sidebar-accent-foreground"
    >
      <Search className="size-4 shrink-0" />
      <span className="flex-1 text-left">Find a client</span>
      <kbd className="hidden rounded border border-sidebar-border px-1.5 font-mono text-xxs text-zinc-500 lg:inline">{isMac ? '⌘K' : 'Ctrl K'}</kbd>
    </button>
  )
}

/**
 * Find a client from anywhere in the admin: by name, email, mobile, ABN or business name.
 * Opens from the sidebar button or Ctrl/⌘+K.
 */
export function ClientSearch({ open, setOpen }: { open: boolean; setOpen: (open: boolean | ((o: boolean) => boolean)) => void }) {
  const [term, setTerm] = useState('')
  const debounced = useDebouncedValue(term.trim(), 250)
  const navigate = useNavigate()

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key.toLowerCase() === 'k' && (e.metaKey || e.ctrlKey)) {
        e.preventDefault()
        setOpen((o) => !o)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [setOpen])

  const { data, isFetching } = useQuery({
    queryKey: ['admin-client-search', debounced],
    queryFn: () => api.admin.clients({ search: debounced, take: 8 }),
    enabled: open && debounced.length >= 2,
    placeholderData: keepPreviousData,
  })
  const items = debounced.length >= 2 ? data?.items ?? [] : []

  const go = (id: string) => {
    setOpen(false)
    setTerm('')
    navigate(`/admin/clients/${id}`)
  }

  return (
    <>
      {/* Results come from the server, so cmdk's own filtering is off. */}
      <CommandDialog open={open} onOpenChange={setOpen} shouldFilter={false} showCloseButton={false} title="Find a client" description="Search by name, email, mobile, ABN or business name">
        <CommandInput value={term} onValueChange={setTerm} placeholder="Name, email, mobile, ABN or business name…" />
        <CommandList>
          {debounced.length < 2 ? (
            <div className="py-6 text-center text-sm text-muted-foreground">Type at least two characters.</div>
          ) : isFetching && items.length === 0 ? (
            <div className="py-6 flex justify-center"><Loader2 className="h-5 w-5 animate-spin text-primary" /></div>
          ) : (
            <CommandEmpty>No client matches “{debounced}”.</CommandEmpty>
          )}
          {items.map((c) => (
            <CommandItem key={c.id} value={c.id} onSelect={() => go(c.id)} className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="truncate text-sm font-medium">{c.fullName || 'Unnamed client'}</div>
                <div className="truncate text-xs text-muted-foreground">
                  {c.email ?? '—'}{c.abns.length ? ` · ${formatAbn(c.abns[0])}` : ''}
                </div>
              </div>
              <div className="shrink-0 text-right text-xs text-muted-foreground">
                {c.activeNames ? `${c.activeNames} name${c.activeNames === 1 ? '' : 's'}` : 'no names'}
                {c.firstName ? <div className="max-w-48 truncate">{c.firstName}</div> : null}
              </div>
            </CommandItem>
          ))}
        </CommandList>
      </CommandDialog>
    </>
  )
}
