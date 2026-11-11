import { sileo } from 'sileo'
import { api, type AsicPortalSettings } from '../../api/client'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Field, SettingsCard, type CardHeaderProps } from './ui'
import { useSave, type FormProps } from './useSave'

/** Our ASIC Connect login, used by "Link to our ASIC account" and by new business names. */
export function AsicPortalCard({ card, value: asicPortal, onChange: setAsicPortal }: { card: CardHeaderProps } & FormProps<AsicPortalSettings>) {
  const save = useSave()
  const onAsicPortal = save('ASIC Connect login', () => api.admin.updateAsicPortalSettings(asicPortal))
  // Tests the saved login (save first): logs in to ASIC Connect and out again.
  const testAsicPortal = () => {
    void sileo.promise(api.admin.testAsicPortalLogin().then((r) => { if (!r.ok) throw new Error(r.message); return r }), {
      loading: { title: 'Logging in to ASIC Connect…' },
      success: (r) => ({ title: 'ASIC Connect login works', description: r.message }),
      error: (e) => ({ title: 'ASIC Connect login failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  return (
    <SettingsCard
      {...card}
      onSubmit={onAsicPortal}
      extra={<Button type="button" variant="outline" onClick={testAsicPortal}>Test saved login</Button>}
    >
      <Field label="Link to our ASIC account" hint={asicPortal.enabled ? 'The button shows on business names with an ASIC key.' : 'Off: the button is turned off.'}>
        <Select value={asicPortal.enabled ? 'on' : 'off'} onValueChange={(v) => setAsicPortal({ ...asicPortal, enabled: v === 'on' })}>
          <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="on">On</SelectItem>
            <SelectItem value="off">Off</SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="Username">
          <Input autoComplete="off" value={asicPortal.username} onChange={(e) => setAsicPortal({ ...asicPortal, username: e.target.value })} />
        </Field>
        <Field label="Password">
          <Input type="password" autoComplete="new-password" value={asicPortal.password} onChange={(e) => setAsicPortal({ ...asicPortal, password: e.target.value })} />
        </Field>
      </div>
      <Field label="Our business email" hint="Put on the business name and on an individual holder.">
        <Input type="email" value={asicPortal.businessEmail} onChange={(e) => setAsicPortal({ ...asicPortal, businessEmail: e.target.value })} />
      </Field>
      <Field label="Representatives to keep" hint="Comma separated. Every other representative is removed."
        tip="A representative is kept when its name contains one of these. Keep our own ASIC Connect account’s person (sdfgsdfg David sdfgsdfg): removing it takes away our access to the name.">
        <Input value={asicPortal.keepRepresentatives} onChange={(e) => setAsicPortal({ ...asicPortal, keepRepresentatives: e.target.value })} />
      </Field>
      <Field label="Declare by default" hint="Each run can pick the other.">
        <Select value={asicPortal.declareAs} onValueChange={(v) => setAsicPortal({ ...asicPortal, declareAs: v as AsicPortalSettings['declareAs'] })}>
          <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="Holder">As the holder</SelectItem>
            <SelectItem value="OnBehalfOfHolder">On behalf of the holder</SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="Ontraport “Did ASIC key link?” field" hint="Set to success or fail.">
          <Input className="font-mono" value={asicPortal.ontraportLinkFieldId} onChange={(e) => setAsicPortal({ ...asicPortal, ontraportLinkFieldId: e.target.value })} />
        </Field>
        <Field label="Wrong-key campaign" hint="Ontraport campaign id; blank = none.">
          <Input className="font-mono" value={asicPortal.wrongKeyCampaignId} onChange={(e) => setAsicPortal({ ...asicPortal, wrongKeyCampaignId: e.target.value })} />
        </Field>
      </div>
    </SettingsCard>
  )
}
