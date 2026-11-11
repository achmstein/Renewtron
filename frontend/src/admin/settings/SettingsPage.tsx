import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import { pageClass, PageHeader } from '../_ui'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeletonizer } from '@/components/skeletonizer'
import { cn } from '@/lib/utils'
import { AsicSettings, AsicTabs } from './AsicSettings'
import { SendGridCard, SendGridSample, WinBackCard } from './EmailCards'
import { OntraportCard } from './OntraportCard'
import { AirwallexCard, PricingCard, StripeCard } from './PaymentCards'
import { TestModeCards } from './TestModeCards'
import { TrackingCard } from './TrackingCard'
import {
  ASIC_TABS, CARDS, SECTIONS, cardStatus, defaultForms, formsFromSettings, sectionStatus,
  type AsicTabKey, type CardKey, type SectionDef, type SectionKey, type SettingsData, type SettingsForms,
} from './model'
import { ConfiguredDot, type CardHeaderProps } from './ui'

// Group sections for the nav
const GROUPS = SECTIONS.reduce<Record<string, SectionDef[]>>((acc, s) => {
  if (!acc[s.group]) acc[s.group] = []
  acc[s.group].push(s)
  return acc
}, {})

export default function Settings() {
  const [activeKey, setActiveKey] = useState<SectionKey>('sendgrid')
  const [asicTab, setAsicTab] = useState<AsicTabKey>('payment')
  // gcTime 0: each visit loads the saved values afresh, since the forms start from them.
  const { data } = useQuery({ queryKey: ['admin-settings'], queryFn: () => api.admin.settings(), gcTime: 0 })

  // The forms are filled once, when the settings arrive; until then the page shows its skeleton.
  return (
    <SettingsScreen key={data ? 'loaded' : 'loading'} data={data}
      activeKey={activeKey} onActiveKeyChange={setActiveKey} asicTab={asicTab} onAsicTabChange={setAsicTab} />
  )
}

function SettingsScreen({ data, activeKey, onActiveKeyChange, asicTab, onAsicTabChange }: {
  data: SettingsData | undefined
  activeKey: SectionKey
  onActiveKeyChange: (k: SectionKey) => void
  asicTab: AsicTabKey
  onAsicTabChange: (k: AsicTabKey) => void
}) {
  const [forms, setForms] = useState<SettingsForms>(() => (data ? formsFromSettings(data) : defaultForms()))
  const setForm = <K extends keyof SettingsForms>(key: K) => (value: SettingsForms[K]) => setForms((f) => ({ ...f, [key]: value }))

  // Configured-status per card and nav entry, from the forms' current values.
  const cards = cardStatus(forms)
  const sections = sectionStatus(cards)
  const cardProps = (key: CardKey): CardHeaderProps => ({ ...CARDS[key], status: cards[key] })

  // While loading, the skeleton wears the header of the card that will show.
  const firstCard: CardKey = activeKey === 'asic' ? ASIC_TABS.find((t) => t.key === asicTab)!.cards[0] : activeKey

  return (
    <div className={pageClass()}>
      <PageHeader title="Settings" subtitle="Integrations, payments and pricing." />

      <div className="grid grid-cols-1 items-start gap-6 lg:grid-cols-4">
        {/* Phones and tablets: the section list is a dropdown, so the form stays at the top of the screen. */}
        <Select value={activeKey} onValueChange={(v) => onActiveKeyChange(v as SectionKey)}>
          <SelectTrigger aria-label="Settings section" className="w-full bg-card lg:hidden">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {Object.entries(GROUPS).map(([group, items]) => (
              <SelectGroup key={group}>
                <SelectLabel>{group}</SelectLabel>
                {items.map((s) => (
                  <SelectItem key={s.key} value={s.key}>
                    {s.title}
                    <ConfiguredDot status={sections[s.key]} />
                  </SelectItem>
                ))}
              </SelectGroup>
            ))}
          </SelectContent>
        </Select>

        {/* Sidebar nav */}
        <Card className="hidden gap-0 py-2 lg:flex">
          <nav className="space-y-3">
            {Object.entries(GROUPS).map(([group, items]) => (
              <div key={group} className="px-2">
                <div className="px-2 py-1 text-caption text-muted-foreground">{group}</div>
                {items.map((s) => {
                  const isActive = s.key === activeKey
                  return (
                    <Button
                      key={s.key}
                      type="button"
                      variant="ghost"
                      size="sm"
                      aria-current={isActive ? 'page' : undefined}
                      onClick={() => onActiveKeyChange(s.key)}
                      className={cn('w-full justify-between font-normal', isActive && 'bg-muted font-medium')}
                    >
                      <span className="truncate">{s.title}</span>
                      <ConfiguredDot status={sections[s.key]} />
                    </Button>
                  )
                })}
              </div>
            ))}
          </nav>
        </Card>

        {/* Active section */}
        <div className="min-w-0 space-y-6 lg:col-span-3">
          {!data ? (
            // While the settings load: the section's real card and fields, as a skeleton.
            <>
              {activeKey === 'asic' ? <AsicTabs value={asicTab} onChange={onAsicTabChange} statuses={cards} /> : null}
              <Skeletonizer loading>
                <SendGridSample card={cardProps(firstCard)} />
              </Skeletonizer>
            </>
          ) : (
            <>
              {activeKey === 'sendgrid' ? <SendGridCard card={cardProps('sendgrid')} value={forms.sendGrid} onChange={setForm('sendGrid')} /> : null}
              {activeKey === 'winback' ? <WinBackCard card={cardProps('winback')} /> : null}
              {activeKey === 'stripe' ? <StripeCard card={cardProps('stripe')} value={forms.stripe} onChange={setForm('stripe')} /> : null}
              {activeKey === 'airwallex' ? <AirwallexCard card={cardProps('airwallex')} value={forms.airwallex} onChange={setForm('airwallex')} /> : null}
              {activeKey === 'pricing' ? <PricingCard card={cardProps('pricing')} value={forms.pricing} onChange={setForm('pricing')} /> : null}
              {activeKey === 'asic' ? (
                <AsicSettings tab={asicTab} onTabChange={onAsicTabChange} forms={forms} setForm={setForm} cardProps={cardProps} statuses={cards} />
              ) : null}
              {activeKey === 'ontraport' ? <OntraportCard card={cardProps('ontraport')} value={forms.ontraport} onChange={setForm('ontraport')} /> : null}
              {activeKey === 'tracking' ? <TrackingCard card={cardProps('tracking')} value={forms.tracking} onChange={setForm('tracking')} /> : null}
              {activeKey === 'testmode' ? <TestModeCards card={cardProps('testmode')} value={forms.testMode} onChange={setForm('testMode')} /> : null}
            </>
          )}
        </div>
      </div>
    </div>
  )
}
