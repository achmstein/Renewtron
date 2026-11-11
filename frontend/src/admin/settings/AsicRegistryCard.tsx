import { sileo } from 'sileo'
import { api } from '../../api/client'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import type { SettingsForms } from './model'
import { Field, SettingsCard, SwitchField, type CardHeaderProps } from './ui'
import { useSave, type FormProps } from './useSave'

/** Nightly due-date lookups on ASIC's business name register (behind reCAPTCHA, so solved through 2Captcha). */
export function AsicRegistryCard({ card, value: registry, onChange: setRegistry }: { card: CardHeaderProps } & FormProps<SettingsForms['asicRegistry']>) {
  const save = useSave()
  // Masked secrets ("••••abcd") go back unchanged: the server keeps what it has.
  const onRegistry = save('Business name register', () => api.admin.updateAsicRegistry(registry))
  // Checks the key in the form (a masked one checks the stored key) and shows the account balance.
  const check2Captcha = () => {
    void sileo.promise(
      api.admin.testAsicRegistry(registry.twoCaptchaApiKey).then((r) => { if (!r.ok) throw new Error(r.error ?? '2Captcha refused the key.'); return r }),
      {
        loading: { title: 'Checking 2Captcha…' },
        success: (r) => ({ title: r.balance != null ? `Balance $${r.balance.toFixed(2)}` : '2Captcha key works' }),
        error: (e) => ({ title: '2Captcha check failed', description: e instanceof Error ? e.message : undefined }),
      },
    ).catch(() => {})
  }
  const num = (v: string) => Math.max(0, Math.floor(Number(v) || 0))

  return (
    <SettingsCard
      {...card}
      onSubmit={onRegistry}
      extra={<Button type="button" variant="outline" onClick={check2Captcha}>Check 2Captcha</Button>}
    >
      <SwitchField id="reg-enabled" label="Enabled" description="Off = due dates fall back to Renewtron’s estimates."
        checked={registry.enabled} onCheckedChange={(v) => setRegistry({ ...registry, enabled: v })} />
      <Field label="2Captcha API key" hint="ASIC’s register search is behind reCAPTCHA; lookups that get a CAPTCHA fail without a key.">
        <Input className="font-mono" autoComplete="off" value={registry.twoCaptchaApiKey} onChange={(e) => setRegistry({ ...registry, twoCaptchaApiKey: e.target.value })} />
      </Field>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="Lookups per nightly run" hint="The most names looked up each night.">
          <Input type="number" min={0} className="font-mono tabular-nums" value={registry.maxLookupsPerRun}
            onChange={(e) => setRegistry({ ...registry, maxLookupsPerRun: num(e.target.value) })} />
        </Field>
        <Field label="Stop after failures in a row" hint="A run stops after this many lookups fail one after another.">
          <Input type="number" min={0} className="font-mono tabular-nums" value={registry.consecutiveFailureLimit}
            onChange={(e) => setRegistry({ ...registry, consecutiveFailureLimit: num(e.target.value) })} />
        </Field>
        <Field label="Recheck after (days)" hint="How old a name’s last lookup gets before it’s looked up again.">
          <Input type="number" min={0} className="font-mono tabular-nums" value={registry.recheckAfterDays}
            onChange={(e) => setRegistry({ ...registry, recheckAfterDays: num(e.target.value) })} />
        </Field>
        <Field label="Recheck when due within (days)" hint="Names due this soon are looked up again sooner.">
          <Input type="number" min={0} className="font-mono tabular-nums" value={registry.recheckWhenDueWithinDays}
            onChange={(e) => setRegistry({ ...registry, recheckWhenDueWithinDays: num(e.target.value) })} />
        </Field>
      </div>
      <Field label="Proxy URL" hint="Optional. Blank = lookups go out directly.">
        <Input className="font-mono" autoComplete="off" value={registry.proxyUrl} onChange={(e) => setRegistry({ ...registry, proxyUrl: e.target.value })} placeholder="http://proxy.example.com:8080" />
      </Field>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="Proxy username">
          <Input autoComplete="off" value={registry.proxyUsername} onChange={(e) => setRegistry({ ...registry, proxyUsername: e.target.value })} />
        </Field>
        <Field label="Proxy password">
          <Input type="password" autoComplete="new-password" value={registry.proxyPassword} onChange={(e) => setRegistry({ ...registry, proxyPassword: e.target.value })} />
        </Field>
      </div>
    </SettingsCard>
  )
}
