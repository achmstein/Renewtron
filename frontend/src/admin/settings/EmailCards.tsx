import { Link } from 'react-router-dom'
import { sileo } from 'sileo'
import { api } from '../../api/client'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import type { SettingsForms } from './model'
import { Field, SettingsCard, type CardHeaderProps } from './ui'
import { useSave, type FormProps } from './useSave'

export function SendGridCard({ card, value: sg, onChange: setSg }: { card: CardHeaderProps } & FormProps<SettingsForms['sendGrid']>) {
  const save = useSave()
  const onSendGrid = save('SendGrid', () => api.admin.updateSendGrid(sg))
  // Sends a test email to the signed-in admin through the saved settings (save first); it shows on the Emails page.
  const testSendGrid = () => {
    void sileo.promise(api.admin.testSendGridEmail().then((r) => { if (!r.ok) throw new Error(r.message); return r }), {
      loading: { title: 'Sending a test email…' },
      success: (r) => ({ title: 'SendGrid took it', description: r.message }),
      error: (e) => ({ title: 'SendGrid didn’t send it', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  return (
    <SettingsCard {...card} onSubmit={onSendGrid}
      extra={<Button type="button" variant="outline" onClick={testSendGrid}>Send test email</Button>}>
      <Field label="API key">
        <Input value={sg.apiKey} onChange={(e) => setSg({ ...sg, apiKey: e.target.value })} />
      </Field>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="From email">
          <Input value={sg.fromEmail} onChange={(e) => setSg({ ...sg, fromEmail: e.target.value })} />
        </Field>
        <Field label="From name">
          <Input value={sg.fromName} onChange={(e) => setSg({ ...sg, fromName: e.target.value })} />
        </Field>
      </div>
      <Field
        label="Event webhook verification key"
        hint={sg.eventWebhookPublicKey ? 'Delivered, bounced and opened show on the Emails page.' : 'Blank: the Emails page shows only whether SendGrid took each email.'}
        tip={`In SendGrid: Settings → Mail Settings → Event Webhook. Post URL ${window.location.origin}/api/sendgrid/events; tick Delivered, Deferred, Bounced, Dropped, Spam reports and Opened; turn on Signed Event Webhook and paste its verification key here.`}
      >
        <Textarea rows={2} className="font-mono text-caption" value={sg.eventWebhookPublicKey} onChange={(e) => setSg({ ...sg, eventWebhookPublicKey: e.target.value })} placeholder="MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…" />
      </Field>
    </SettingsCard>
  )
}

/** While the settings load: the SendGrid card and its fields, as sample content for the skeleton. */
export function SendGridSample({ card }: { card: CardHeaderProps }) {
  return (
    <SettingsCard {...card} onSubmit={(e) => e.preventDefault()}>
      <Field label="API key"><Input defaultValue="SG.xxxxxxxxxxxxxxxxxxxxxxxx" /></Field>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="From email"><Input defaultValue="hello@example.com.au" /></Field>
        <Field label="From name"><Input defaultValue="Ideal Business" /></Field>
      </div>
      <Field label="Webhook key" hint="Delivered, bounced and opened show on the Emails page."><Textarea rows={2} /></Field>
    </SettingsCard>
  )
}

/** The win-back wording moved to Emails → Templates, with every other customer email. */
export function WinBackCard({ card }: { card: CardHeaderProps }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>{card.title}</CardTitle>
        <CardDescription>The win-back email is now edited with the other customer emails, with a live preview and history.</CardDescription>
      </CardHeader>
      <CardContent>
        <Button asChild size="sm"><Link to="/admin/emails/templates/win-back">Edit the win-back email</Link></Button>
      </CardContent>
    </Card>
  )
}
