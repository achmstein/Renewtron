import { Check, PlusCircle } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Command, CommandEmpty, CommandGroup, CommandItem, CommandList, CommandSeparator,
} from '@/components/ui/command'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Separator } from '@/components/ui/separator'
import { cn } from '@/lib/utils'

export type FacetOption = { value: string; label: string; count: number }

/**
 * shadcn-style faceted filter, but the facts come from the server: options and their
 * counts are computed by the API under the other active filters, and selecting values
 * refetches server-side — nothing is filtered in the browser.
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
      <PopoverContent className="w-56 p-0" align="start">
        <Command>
          <CommandList>
            <CommandEmpty>Nothing to filter.</CommandEmpty>
            <CommandGroup>
              {options.map((option) => {
                const isSelected = selectedSet.has(option.value)
                return (
                  <CommandItem key={option.value} onSelect={() => toggle(option.value)}>
                    <div className={cn(
                      'flex size-4 items-center justify-center rounded-[4px] border',
                      isSelected ? 'border-primary bg-primary text-primary-foreground' : 'border-input [&_svg]:invisible',
                    )}>
                      <Check className="size-3.5" />
                    </div>
                    <span>{option.label}</span>
                    <span className="ml-auto font-mono text-xs tabular-nums text-muted-foreground">{option.count}</span>
                  </CommandItem>
                )
              })}
            </CommandGroup>
            {selected.length > 0 ? (
              <>
                <CommandSeparator />
                <CommandGroup>
                  <CommandItem onSelect={() => onChange([])} className="justify-center text-center">Clear</CommandItem>
                </CommandGroup>
              </>
            ) : null}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
