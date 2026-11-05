import { Check, PlusCircle } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Command, CommandEmpty, CommandGroup, CommandItem, CommandList,
} from '@/components/ui/command'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Separator } from '@/components/ui/separator'
import { cn } from '@/lib/utils'

export type FacetOption = { value: string; label: string; count: number }

/**
 * shadcn-style faceted filter, but the facts come from the server: options and their
 * counts are computed by the API under the other active filters, and selecting values
 * refetches server-side — nothing is filtered in the browser.
 * The popup is styled to the admin's language (rounded-xl, zinc ring, mono kicker)
 * rather than the stock shadcn look.
 */
export function FacetedFilter({ title, options, selected, onChange }: {
  title: string
  options: FacetOption[]
  selected: string[]
  onChange: (values: string[]) => void
}) {
  const selectedSet = new Set(selected)
  const toggle = (value: string) => {
    const next = new Set(selectedSet)
    if (next.has(value)) next.delete(value)
    else next.add(value)
    onChange([...next])
  }

  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" className="h-9 border-dashed whitespace-nowrap">
          <PlusCircle className="h-4 w-4" />
          {title}
          {selected.length > 0 ? (
            <>
              <Separator orientation="vertical" className="mx-0.5 h-4" />
              <div className="flex gap-1">
                {selected.length > 2 ? (
                  <Badge variant="secondary" className="rounded-sm px-1 font-mono font-normal">{selected.length} selected</Badge>
                ) : options.filter(o => selectedSet.has(o.value)).map(o => (
                  <Badge key={o.value} variant="secondary" className="rounded-sm px-1 font-mono font-normal">{o.label}</Badge>
                ))}
              </div>
            </>
          ) : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-60 rounded-xl border-border p-0 shadow-lg" align="start">
        <div className="border-b border-border px-3 pb-2 pt-2.5">
          <div className="text-xs font-medium text-muted-foreground">{title}</div>
        </div>
        <Command className="rounded-xl">
          <CommandList>
            <CommandEmpty className="px-3 py-6 text-center text-sm text-muted-foreground">Nothing to filter.</CommandEmpty>
            <CommandGroup className="p-1.5">
              {options.map((option) => {
                const isSelected = selectedSet.has(option.value)
                return (
                  <CommandItem
                    key={option.value}
                    onSelect={() => toggle(option.value)}
                    className="cursor-pointer gap-2.5 rounded-md px-2.5 py-2 text-foreground data-[selected=true]:bg-muted/50 data-[selected=true]:text-foreground"
                  >
                    <span className={cn(
                      'flex size-4 shrink-0 items-center justify-center rounded-[4px] ring-1 ring-inset transition-colors',
                      isSelected ? 'bg-primary ring-brand-600 text-white' : 'bg-card ring-border [&_svg]:invisible',
                    )}>
                      <Check className="size-3 !text-white" strokeWidth={3} />
                    </span>
                    <span className="truncate">{option.label}</span>
                    <span className="ml-auto shrink-0 font-mono text-xs tabular-nums text-muted-foreground/70">{option.count.toLocaleString()}</span>
                  </CommandItem>
                )
              })}
            </CommandGroup>
          </CommandList>
        </Command>
        {selected.length > 0 ? (
          <div className="border-t border-border p-1.5">
            <button
              type="button"
              onClick={() => onChange([])}
              className="w-full rounded-md px-2.5 py-1.5 text-center text-sm font-medium text-muted-foreground transition-colors hover:bg-muted/50 hover:text-foreground"
            >
              Clear {title.toLowerCase()}
            </button>
          </div>
        ) : null}
      </PopoverContent>
    </Popover>
  )
}
