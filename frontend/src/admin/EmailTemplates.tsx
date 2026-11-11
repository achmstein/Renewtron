import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ArrowLeft, ChevronRight, LayoutTemplate } from 'lucide-react'
import { templatesApi, type TemplateSummary } from '../api/emailTemplates'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { sample } from '@/components/skeleton-samples'
import { sampleRows, Skeletonizer } from '@/components/skeletonizer'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { pageClass, PageHeader } from './_ui'
import { relativeTime } from './_utils'

/*
 * Emails → Templates (/admin/emails/templates): every email Renewtron sends a customer, whether it's
 * on, and which wording it uses. Opening one goes to the editor.
 */

const SKELETON = sampleRows<TemplateSummary>(6, (i) => ({
  key: `sample-${i}`,
  name: ['Renewal confirmation', 'Payment received', 'Renewal reminder', 'ASIC key request', 'Win-back', 'Renewal failed'][i],
  description: ['Sent when ASIC confirms the renewal.', 'Sent the moment a card payment goes through.', 'Sent before the business name falls due.', 'Asks the client for their ASIC key.', 'Brings back clients who stopped part way.', 'Tells the client something went wrong.'][i],
  enabled: true, version: i % 3, updatedAt: sample.daysAgo(i * 5), updatedBy: 'david@example.com.au', lastSentAt: sample.daysAgo(i),
}))

export default function EmailTemplates() {
  const q = useQuery({ queryKey: ['email-templates'], queryFn: templatesApi.list })

  return (
    <div className={pageClass('py-8')}>
      <Button asChild variant="ghost" size="sm" className="-ml-2 mb-2">
        <Link to="/admin/emails"><ArrowLeft /> Emails</Link>
      </Button>
      <PageHeader title="Email templates" subtitle="What each customer email says. Changes apply from the next email sent." />

      {q.error ? <Alert variant="destructive"><AlertDescription>{(q.error as Error).message}</AlertDescription></Alert> : null}

      <Skeletonizer loading={q.isPending} className="mt-6">
        <Card className="gap-0 py-0">
          {(q.data?.templates ?? (q.isPending ? SKELETON : [])).map((t) => <TemplateRow key={t.key} t={t} />)}
        </Card>
      </Skeletonizer>

      {q.data && (
        <Card className="mt-6 gap-0 py-0">
          <TemplateRow t={q.data.layout} icon />
        </Card>
      )}
    </div>
  )
}

function TemplateRow({ t, icon }: { t: TemplateSummary; icon?: boolean }) {
  const wording = t.version === 0
    ? 'Default wording'
    : `Version ${t.version}${t.updatedAt ? ` · ${t.updatedBy ? `${t.updatedBy.split('@')[0]}, ` : ''}${relativeTime(t.updatedAt)}` : ''}`
  return (
    <Link
      to={`/admin/emails/templates/${encodeURIComponent(t.key)}`}
      className="flex items-center gap-3 border-b px-4 py-3.5 transition-colors last:border-0 hover:bg-muted/50"
    >
      {icon && <LayoutTemplate className="size-4 shrink-0 text-muted-foreground" />}
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm font-medium">{t.name}</span>
          {!t.enabled && <Badge variant="outline">Off</Badge>}
        </div>
        <div className="truncate text-caption text-muted-foreground">{t.description}</div>
      </div>
      <div className="hidden shrink-0 text-right text-caption text-muted-foreground sm:block">
        <div>{wording}</div>
        {t.lastSentAt && <div>Last sent {relativeTime(t.lastSentAt)}</div>}
      </div>
      <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
    </Link>
  )
}
