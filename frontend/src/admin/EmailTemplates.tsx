import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ArrowLeft, ChevronRight, LayoutTemplate } from 'lucide-react'
import { templatesApi, type TemplateSummary } from '../api/emailTemplates'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { PageHeader } from './_ui'
import { relativeTime } from './_utils'

/*
 * Emails → Templates (/admin/emails/templates): every email Renewtron sends a customer, whether it's
 * on, and which wording it uses. Opening one goes to the editor.
 */

export default function EmailTemplates() {
  const q = useQuery({ queryKey: ['email-templates'], queryFn: templatesApi.list })

  return (
    <div className="mx-auto max-w-5xl px-4 py-8 sm:px-6 lg:px-8">
      <Button asChild variant="ghost" size="sm" className="-ml-2 mb-2">
        <Link to="/admin/emails"><ArrowLeft /> Emails</Link>
      </Button>
      <PageHeader title="Email templates" subtitle="What each customer email says. Changes apply from the next email sent." />

      {q.error ? <Alert variant="destructive"><AlertDescription>{(q.error as Error).message}</AlertDescription></Alert> : null}

      <Card className="mt-6 gap-0 py-0">
        {q.isLoading
          ? Array.from({ length: 5 }).map((_, i) => <div key={i} className="border-b p-4 last:border-0"><Skeleton className="h-10" /></div>)
          : q.data?.templates.map((t) => <TemplateRow key={t.key} t={t} />)}
      </Card>

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
