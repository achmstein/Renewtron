import type { ReactNode } from 'react'
import { Info } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { cn } from '@/lib/utils'
import { STATUS_DOT, STATUS_WORD, type CardDef, type ConfigStatus } from './model'

/** What every section card is given: its title, one line, tip and configured-status. */
export type CardHeaderProps = CardDef & { status: ConfigStatus }

export function ConfiguredDot({ status }: { status: ConfigStatus }) {
  return <span aria-label={STATUS_WORD[status]} title={STATUS_WORD[status]} className={cn('size-1.5 shrink-0 rounded-full', STATUS_DOT[status])} />
}

/** One settings section: title, one line, the fields, and Save on the right. */
export function SettingsCard({ title, description, tip, status, onSubmit, saveLabel = 'Save', extra, after, children }: CardHeaderProps & {
  onSubmit: (e: React.FormEvent) => void
  saveLabel?: string
  extra?: ReactNode
  after?: ReactNode
  children: ReactNode
}) {
  return (
    <form onSubmit={onSubmit}>
      <Card>
        <CardHeader>
          <CardTitle>{title}</CardTitle>
          <CardDescription title={tip}>
            {description}
            {tip ? <Info aria-hidden className="ml-1 inline size-3.5 -translate-y-px" /> : null}
          </CardDescription>
          <CardAction>
            <span className="flex items-center gap-1.5 text-caption text-muted-foreground">
              <span aria-hidden className={cn('size-2 rounded-full', STATUS_DOT[status])} />
              {STATUS_WORD[status]}
            </span>
          </CardAction>
        </CardHeader>
        <CardContent className="space-y-4">
          {children}
          {after}
        </CardContent>
        <CardFooter className="justify-end gap-2 border-t">
          {extra}
          <Button type="submit">{saveLabel}</Button>
        </CardFooter>
      </Card>
    </form>
  )
}

/** A label, its input, and at most one muted line under it; the longer explanation on hover. */
export function Field({ label, hint, tip, children }: { label: string; hint?: string; tip?: string; children: ReactNode }) {
  return (
    <div className="grid content-start gap-1.5">
      <Label title={tip}>
        {label}
        {tip ? <Info aria-hidden className="size-3.5 text-muted-foreground" /> : null}
      </Label>
      {children}
      {hint ? <p className="text-caption text-muted-foreground">{hint}</p> : null}
    </div>
  )
}

/** An on/off setting: label and one line on the left, the switch on the right. */
export function SwitchField({ id, label, description, checked, onCheckedChange }: {
  id: string
  label: string
  description: string
  checked: boolean
  onCheckedChange: (v: boolean) => void
}) {
  return (
    <div className="flex items-center justify-between gap-4 rounded-lg border px-4 py-3">
      <div className="grid gap-0.5">
        <Label htmlFor={id}>{label}</Label>
        <p className="text-caption text-muted-foreground">{description}</p>
      </div>
      <Switch id={id} checked={checked} onCheckedChange={onCheckedChange} />
    </div>
  )
}

/** A dollar amount input. */
export function MoneyInput({ value, onChange }: { value: number; onChange: (n: number) => void }) {
  return (
    <div className="relative">
      <span className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 text-sm text-muted-foreground">$</span>
      <Input type="number" step="0.01" className="pl-7 font-mono tabular-nums" value={value} onChange={(e) => onChange(Number(e.target.value))} />
    </div>
  )
}

/** A small text action under a field (e.g. reset to the default). */
export function LinkAction({ onClick, children }: { onClick: () => void; children: ReactNode }) {
  return (
    <Button type="button" variant="link" onClick={onClick} className="h-auto justify-self-start p-0 text-caption font-normal text-link">
      {children}
    </Button>
  )
}
