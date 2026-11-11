import { useQuery, useQueryClient } from '@tanstack/react-query'
import { sileo } from 'sileo'
import { api } from '../../api/client'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import type { SettingsForms } from './model'
import { Field, SettingsCard, type CardHeaderProps } from './ui'
import { useSave, type FormProps } from './useSave'

export function TestModeCards({ card, value: testMode, onChange: setTestMode }: { card: CardHeaderProps } & FormProps<SettingsForms['testMode']>) {
  const queryClient = useQueryClient()
  const save = useSave()
  const onTestMode = save('Test mode', () => api.admin.updateTestMode(testMode))
  const { data: testData } = useQuery({ queryKey: ['admin-test-mode-data'], queryFn: () => api.admin.testModeData() })

  const deleteTestData = () => {
    if (!window.confirm('Delete every test lead, renewal and ASIC key request? Real customers are not touched.')) return
    void sileo.promise(api.admin.deleteTestModeData(), {
      loading: { title: 'Deleting test data…' },
      success: (r) => {
        void queryClient.invalidateQueries({ queryKey: ['admin-test-mode-data'] })
        return { title: 'Test data deleted', description: `${r.leads} lead(s), ${r.renewals} renewal(s), ${r.asicKeyRequests} key request(s).` }
      },
      error: (err) => ({ title: 'Could not delete test data', description: err instanceof Error ? err.message : undefined }),
    }).catch(() => {})
  }

  return (
    <>
      <SettingsCard {...card} onSubmit={onTestMode}>
        <Field label="Test customer emails" hint="One per line, e.g. you+test@gmail.com. Never a real client’s address.">
          <Textarea rows={3} className="font-mono" value={testMode.testEmails} onChange={(e) => setTestMode({ ...testMode, testEmails: e.target.value })} />
        </Field>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="Stripe test secret key" hint="Starts sk_test_." tip="Stripe dashboard → Developers → API keys, with 'Test mode' switched on. Starts sk_test_.">
            <Input type="password" className="font-mono" value={testMode.stripeSecretKey} onChange={(e) => setTestMode({ ...testMode, stripeSecretKey: e.target.value })} />
          </Field>
          <Field label="Stripe test publishable key" hint="Starts pk_test_.">
            <Input className="font-mono" value={testMode.stripePublishableKey} onChange={(e) => setTestMode({ ...testMode, stripePublishableKey: e.target.value })} />
          </Field>
        </div>
      </SettingsCard>
      <Card>
        <CardHeader>
          <CardTitle>Test data</CardTitle>
          <CardDescription title="The Business Portal keeps its own copy of the test account.">
            {testData
              ? `${testData.leads} lead(s), ${testData.renewals} renewal(s), ${testData.asicKeyRequests} key request(s) on file.`
              : 'Loading test data…'}
          </CardDescription>
          <CardAction>
            <Button type="button" variant="outline" size="sm" onClick={deleteTestData}>Delete test data</Button>
          </CardAction>
        </CardHeader>
      </Card>
    </>
  )
}
