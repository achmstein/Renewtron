import { api } from '../../api/client'
import { Input } from '@/components/ui/input'
import type { SettingsForms } from './model'
import { Field, SettingsCard, SwitchField, type CardHeaderProps } from './ui'
import { useSave, type FormProps } from './useSave'

export function OntraportCard({ card, value: ontraport, onChange: setOntraport }: { card: CardHeaderProps } & FormProps<SettingsForms['ontraport']>) {
  const save = useSave()
  const onOntraport = save('Ontraport', () => api.admin.updateOntraport(ontraport))
  return (
    <SettingsCard {...card} onSubmit={onOntraport}>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="API app ID">
          <Input className="font-mono" value={ontraport.apiAppId} onChange={(e) => setOntraport({ ...ontraport, apiAppId: e.target.value })} />
        </Field>
        <Field label="API key">
          <Input className="font-mono" value={ontraport.apiKey} onChange={(e) => setOntraport({ ...ontraport, apiKey: e.target.value })} />
        </Field>
      </div>
      <Field label="Conversation ID">
        <Input className="font-mono" value={ontraport.conversationId} onChange={(e) => setOntraport({ ...ontraport, conversationId: e.target.value })} />
      </Field>
      <SwitchField id="op-push" label="Add wizard users to Ontraport" description="Create or update a contact for everyone who uses the renewal wizard." checked={ontraport.pushWizardContacts} onCheckedChange={(v) => setOntraport({ ...ontraport, pushWizardContacts: v })} />
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="Wizard form tag ID">
          <Input className="font-mono" value={ontraport.wizardLeadTagId ?? ''} onChange={(e) => setOntraport({ ...ontraport, wizardLeadTagId: e.target.value })} placeholder="1908" />
        </Field>
        <Field label="Wizard paid tag ID">
          <Input className="font-mono" value={ontraport.wizardPaidTagId ?? ''} onChange={(e) => setOntraport({ ...ontraport, wizardPaidTagId: e.target.value })} placeholder="1737" />
        </Field>
      </div>
    </SettingsCard>
  )
}
