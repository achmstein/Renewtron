import { useState, type ReactNode } from 'react'
import { useMutation } from '@tanstack/react-query'
import { sileo } from 'sileo'
import { api, type AsicKeyInboxSettings, type AsicKeyInboxTestResult, type AsicKeyRequestSettings } from '../../api/client'
import { relativeTime } from '../_utils'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { cn } from '@/lib/utils'
import { Field, LinkAction, SettingsCard, SwitchField, type CardHeaderProps } from './ui'
import { useSave, type FormProps } from './useSave'

/** Queuing ASIC key requests for the key tool. */
export function AsicKeyRequestsCard({ card, value: keyRequests, onChange: setKeyRequests }: { card: CardHeaderProps } & FormProps<AsicKeyRequestSettings>) {
  const save = useSave()
  const onKeyRequests = save('ASIC key requests', () => api.admin.updateAsicKeyRequest(keyRequests))
  return (
    <SettingsCard {...card} onSubmit={onKeyRequests}>
      <SwitchField id="kr-enabled" label="Requests enabled" description="Queue ASIC key requests for the key tool." checked={keyRequests.enabled} onCheckedChange={(v) => setKeyRequests({ ...keyRequests, enabled: v })} />
      <SwitchField id="kr-auto" label="Request on Ontraport sync" description="Queue a request for every eligible sale the sync brings in." checked={keyRequests.autoRequestOnSync} onCheckedChange={(v) => setKeyRequests({ ...keyRequests, autoRequestOnSync: v })} />
      <Field label="Email the key to">
        <Input type="email" value={keyRequests.requestEmail} onChange={(e) => setKeyRequests({ ...keyRequests, requestEmail: e.target.value })} />
      </Field>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <Field label="Area code">
          <Input className="font-mono tabular-nums" value={keyRequests.defaultPhonePrefix} onChange={(e) => setKeyRequests({ ...keyRequests, defaultPhonePrefix: e.target.value })} placeholder="02" />
        </Field>
        <div className="sm:col-span-2">
          <Field label="Fallback phone">
            <Input className="font-mono tabular-nums" value={keyRequests.defaultPhoneNumber} onChange={(e) => setKeyRequests({ ...keyRequests, defaultPhoneNumber: e.target.value })} />
          </Field>
        </div>
      </div>
      <Field label="Enquiry text">
        <Textarea rows={3} className="font-mono" value={keyRequests.messageTemplate} onChange={(e) => setKeyRequests({ ...keyRequests, messageTemplate: e.target.value })} />
        {keyRequests.defaultMessageTemplate && keyRequests.messageTemplate !== keyRequests.defaultMessageTemplate ? (
          <LinkAction onClick={() => setKeyRequests({ ...keyRequests, messageTemplate: keyRequests.defaultMessageTemplate! })}>Use the default wording</LinkAction>
        ) : null}
      </Field>
    </SettingsCard>
  )
}

/** Reading ASIC key letters from a Gmail inbox, with a test of the form's current values. */
export function AsicKeyInboxCard({ card, value: asicKeys, onChange: setAsicKeys }: { card: CardHeaderProps } & FormProps<AsicKeyInboxSettings>) {
  const save = useSave()
  const onAsicKeys = save('ASIC key inbox', () => api.admin.updateAsicKeyInbox(asicKeys))
  const [asicKeysTest, setAsicKeysTest] = useState<AsicKeyInboxTestResult | null>(null)

  // Tests the form's current values without saving them; each check reports on its own line.
  const testMutation = useMutation({ mutationFn: () => api.admin.testAsicKeyInbox(asicKeys) })
  const testAsicKeys = () => {
    setAsicKeysTest(null)
    void sileo.promise(testMutation.mutateAsync(), {
      loading: { title: 'Testing inbox, Ontraport field and pattern…' },
      success: (r) => {
        setAsicKeysTest(r)
        const allOk = r.mailbox.ok && r.ontraportField.ok && r.keyPattern.ok
        return { title: allOk ? 'All checks passed' : 'Some checks failed', description: allOk ? 'Save to keep these values.' : 'See the results under the form.' }
      },
      error: (err) => ({ title: 'Test failed', description: err instanceof Error ? err.message : undefined }),
    }).catch(() => {})
  }

  return (
    <SettingsCard
      {...card}
      onSubmit={onAsicKeys}
      extra={
        <Button type="button" variant="outline" onClick={testAsicKeys} disabled={testMutation.isPending}>
          {testMutation.isPending ? 'Testing…' : 'Test connection'}
        </Button>
      }
      after={asicKeysTest ? <AsicKeyTestResults result={asicKeysTest} /> : null}
    >
      <SwitchField id="ak-enabled" label="Scanning enabled" description="Read new ASIC key letters from this inbox." checked={asicKeys.enabled} onCheckedChange={(v) => setAsicKeys({ ...asicKeys, enabled: v })} />
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-4">
        <div className="sm:col-span-3">
          <Field label="IMAP host">
            <Input className="font-mono" value={asicKeys.imapHost} onChange={(e) => setAsicKeys({ ...asicKeys, imapHost: e.target.value })} placeholder="imap.gmail.com" />
          </Field>
        </div>
        <Field label="Port">
          <Input type="number" className="font-mono tabular-nums" value={asicKeys.imapPort} onChange={(e) => setAsicKeys({ ...asicKeys, imapPort: Number(e.target.value) || 993 })} />
        </Field>
      </div>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="Gmail address">
          <Input type="email" value={asicKeys.username} onChange={(e) => setAsicKeys({ ...asicKeys, username: e.target.value })} />
        </Field>
        <Field label="App password">
          <Input type="password" className="font-mono" value={asicKeys.password} onChange={(e) => setAsicKeys({ ...asicKeys, password: e.target.value })} />
        </Field>
      </div>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <Field label="Folder or label">
          <Input className="font-mono" value={asicKeys.folder} onChange={(e) => setAsicKeys({ ...asicKeys, folder: e.target.value })} placeholder="INBOX" />
        </Field>
        <Field label="Subject contains">
          <Input value={asicKeys.subjectFilter} onChange={(e) => setAsicKeys({ ...asicKeys, subjectFilter: e.target.value })} />
        </Field>
        <Field label="Lookback (days)">
          <Input type="number" min={1} max={365} className="font-mono tabular-nums" value={asicKeys.lookbackDays} onChange={(e) => setAsicKeys({ ...asicKeys, lookbackDays: Number(e.target.value) || 30 })} />
        </Field>
      </div>
      <Field label="Ontraport ASIC key field">
        <Input className="font-mono" value={asicKeys.ontraportFieldId} onChange={(e) => setAsicKeys({ ...asicKeys, ontraportFieldId: e.target.value })} placeholder="f5xxx" />
      </Field>
      <Field label="ASIC key pattern">
        <Input className="font-mono" value={asicKeys.asicKeyPattern} onChange={(e) => setAsicKeys({ ...asicKeys, asicKeyPattern: e.target.value })} />
        {asicKeys.defaultAsicKeyPattern && asicKeys.asicKeyPattern !== asicKeys.defaultAsicKeyPattern ? (
          <LinkAction onClick={() => setAsicKeys({ ...asicKeys, asicKeyPattern: asicKeys.defaultAsicKeyPattern! })}>Use the current default pattern</LinkAction>
        ) : null}
      </Field>
    </SettingsCard>
  )
}

function AsicKeyTestResults({ result }: { result: AsicKeyInboxTestResult }) {
  const { mailbox, ontraportField, keyPattern } = result
  return (
    <div className="divide-y rounded-lg border text-sm">
      <TestRow ok={mailbox.ok} label="Mailbox">
        {mailbox.ok ? (
          <>
            Signed in to <span className="font-mono">{mailbox.host}</span> as <span className="font-mono">{mailbox.username}</span>.{' '}
            <span className="font-mono">{mailbox.folder}</span> holds {mailbox.messagesInFolder.toLocaleString()} messages;{' '}
            <span className="font-medium tabular-nums">{mailbox.matchingInLookback}</span> match the subject filter in the lookback window
            {mailbox.latestSubject ? <> — latest: “{mailbox.latestSubject}”{mailbox.latestReceivedAt ? ` (${relativeTime(mailbox.latestReceivedAt)})` : ''}</> : null}.
          </>
        ) : mailbox.error}
      </TestRow>
      <TestRow ok={ontraportField.ok} label="Ontraport field">
        {ontraportField.ok
          ? <>Field <span className="font-mono">{ontraportField.fieldId}</span> exists on Contacts as “{ontraportField.alias}”.</>
          : ontraportField.error}
      </TestRow>
      <TestRow ok={keyPattern.ok} label="Key pattern">
        {keyPattern.ok
          ? <>Extracts <span className="font-mono">{keyPattern.sampleKey}</span> from the sample letter wording.</>
          : keyPattern.error}
      </TestRow>
    </div>
  )
}

function TestRow({ ok, label, children }: { ok: boolean; label: string; children: ReactNode }) {
  return (
    <div className="flex items-start gap-3 px-4 py-3">
      <span aria-hidden className={cn('mt-1.5 size-2 shrink-0 rounded-full', ok ? 'bg-success' : 'bg-destructive')} />
      <div className="min-w-0">
        <div className="font-medium">{label} <span className="font-normal text-muted-foreground">· {ok ? 'passed' : 'failed'}</span></div>
        <div className="mt-0.5 break-words text-caption text-muted-foreground">{children}</div>
      </div>
    </div>
  )
}
