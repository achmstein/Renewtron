import { ViewSwitch } from '../_ui'
import { AsicKeyInboxCard, AsicKeyRequestsCard } from './AsicKeyCards'
import { AsicPaymentCard } from './AsicPaymentCard'
import { AsicPortalCard } from './AsicPortalCard'
import { AsicRegistryCard } from './AsicRegistryCard'
import { RegistrationCard } from './RegistrationCard'
import { ASIC_TABS, combinedStatus, type AsicTabKey, type CardKey, type ConfigStatus, type SettingsForms } from './model'
import { ConfiguredDot, type CardHeaderProps } from './ui'

/** The tabs of the ASIC area, each with a dot for how configured its cards are. */
export function AsicTabs({ value, onChange, statuses }: { value: AsicTabKey; onChange: (v: AsicTabKey) => void; statuses: Record<CardKey, ConfigStatus> }) {
  return (
    <ViewSwitch<AsicTabKey> label="ASIC settings" value={value} onChange={onChange}
      options={ASIC_TABS.map((t) => ({
        value: t.key,
        label: <>{t.label}<ConfiguredDot status={combinedStatus(t.cards.map((k) => statuses[k]))} /></>,
      }))} />
  )
}

/** Everything Renewtron does at ASIC, one tab at a time. Each card saves on its own, as before. */
export function AsicSettings({ tab, onTabChange, forms, setForm, cardProps, statuses }: {
  tab: AsicTabKey
  onTabChange: (v: AsicTabKey) => void
  forms: SettingsForms
  setForm: <K extends keyof SettingsForms>(key: K) => (value: SettingsForms[K]) => void
  cardProps: (key: CardKey) => CardHeaderProps
  statuses: Record<CardKey, ConfigStatus>
}) {
  return (
    <>
      <AsicTabs value={tab} onChange={onTabChange} statuses={statuses} />
      {tab === 'payment' ? (
        <AsicPaymentCard card={cardProps('asicpayment')} value={forms.asic} onChange={setForm('asic')}
          hasCvc={forms.asicHasCvc} onCvcStored={() => setForm('asicHasCvc')(true)} />
      ) : null}
      {tab === 'register' ? <AsicRegistryCard card={cardProps('asicregistry')} value={forms.asicRegistry} onChange={setForm('asicRegistry')} /> : null}
      {tab === 'portal' ? <AsicPortalCard card={cardProps('asicportal')} value={forms.asicPortal} onChange={setForm('asicPortal')} /> : null}
      {tab === 'keys' ? (
        <>
          <AsicKeyRequestsCard card={cardProps('asickeyrequests')} value={forms.asicKeyRequest} onChange={setForm('asicKeyRequest')} />
          <AsicKeyInboxCard card={cardProps('asickeys')} value={forms.asicKeyInbox} onChange={setForm('asicKeyInbox')} />
        </>
      ) : null}
      {tab === 'registration' ? <RegistrationCard card={cardProps('registration')} value={forms.registration} onChange={setForm('registration')} /> : null}
    </>
  )
}
