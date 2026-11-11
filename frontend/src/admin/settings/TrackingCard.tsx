import { api } from '../../api/client'
import { Input } from '@/components/ui/input'
import type { SettingsForms } from './model'
import { Field, SettingsCard, type CardHeaderProps } from './ui'
import { useSave, type FormProps } from './useSave'

export function TrackingCard({ card, value: tracking, onChange: setTracking }: { card: CardHeaderProps } & FormProps<SettingsForms['tracking']>) {
  const save = useSave()
  const onTracking = save('Tracking tags', () => api.admin.updateTracking(tracking))
  return (
    <SettingsCard {...card} onSubmit={onTracking}>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="Google Tag Manager container">
          <Input className="font-mono" value={tracking.gtmContainerId} onChange={(e) => setTracking({ ...tracking, gtmContainerId: e.target.value })} placeholder="GTM-XXXXXXX" />
        </Field>
        <Field label="GA4 measurement ID">
          <Input className="font-mono" value={tracking.ga4MeasurementId} onChange={(e) => setTracking({ ...tracking, ga4MeasurementId: e.target.value })} placeholder="G-XXXXXXXXXX" />
        </Field>
        <Field label="Meta pixel ID">
          <Input className="font-mono" value={tracking.metaPixelId} onChange={(e) => setTracking({ ...tracking, metaPixelId: e.target.value })} placeholder="1234567890" />
        </Field>
        <Field label="Microsoft Clarity project ID">
          <Input className="font-mono" value={tracking.clarityProjectId} onChange={(e) => setTracking({ ...tracking, clarityProjectId: e.target.value.trim() })} placeholder="abcd1234ef" />
        </Field>
      </div>
    </SettingsCard>
  )
}
