import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { TriangleAlert } from 'lucide-react'
import { api, type DashboardResponse, type OverviewCharts } from '../api/client'
import { AheadChart, AsicChart, ChannelChart, OutcomeChart } from './_overviewCharts'
import { Hint, pageClass, PageHeader, Stat, StatStrip } from './_ui'
import { fmtMoney0, relativeTime } from './_utils'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Card } from '@/components/ui/card'
import { sample } from '@/components/skeleton-samples'
import { Skeletonizer } from '@/components/skeletonizer'
import { cn } from '@/lib/utils'

/*
 * Reports › Overview: the money worth chasing, how the system is running, and this month's
 * numbers. The day's work itself is on Today. Quiet stats (see STYLE.md): black numbers, a dot
 * only where a status matters, related numbers in one card.
 */

/** Shown as a skeleton while the first load is on its way: the page's real cards with sample numbers. */
const SKELETON: DashboardResponse = {
  stats: {
    totalSearches: 1840, successfulSearches: 1620, renewalsInitiated: 412, renewalsCompleted: 388, renewalsPending: 12, renewalsFailed: 9,
    totalLeads: 960, convertedLeads: 214, notDueLeads: 120, renewtronDirectCount: 142, renewtronDirectRevenue: 42_318.5,
    ontraportCount: 96, ontraportRevenue: 28_704, avgBasket: 298, abandonedAtPaymentCount: 38, abandonedAtPaymentValue: 11_362,
    ontraportPipelineCount: 54, ontraportPipelineValue: 16_146, failedRenewalCount: 6, failedRenewalValue: 1_794,
    pastCustomersDueSoonCount: 210, pastCustomersDueSoonValue: 62_790,
  },
  activity: [],
  recentSearches: [],
  recentRenewals: [],
  health: { otpProblem: null, otpHeldCount: 0, stuckCount: 0, oldestStuckAt: null, outboxPending: 2, outboxDead: 0, queueDepth: 3, recurringJobs: null },
}

/** Sample series drawn as a skeleton while the charts load. */
const SKELETON_CHARTS: OverviewCharts = {
  weeks: Array.from({ length: 26 }, (_, i) => ({
    week: sample.daysAgo((25 - i) * 7).slice(0, 10), direct: 3000 + (i % 5) * 400, ontraport: 2000 + (i % 4) * 300, manual: 0,
    renewed: 4000 + (i % 3) * 300, underWay: i > 20 ? 800 : 0, needsPerson: i % 6 === 0 ? 300 : 0, closed: 150, refunded: 0,
  })),
  asic: Array.from({ length: 60 }, (_, i) => ({ date: sample.daysAgo(59 - i).slice(0, 10), card: 6 + (i % 4), bpay: i > 40 ? 5 : 0, failed: i % 7 === 0 ? 2 : 0 })),
  ahead: Array.from({ length: 12 }, (_, i) => ({ week: sample.daysAhead(i * 7).slice(0, 10), paid: 4 + (i % 3), notPaid: 20 + (i % 5) * 3 })),
  headline: { paidThisMonth: 18_400, paidLastMonthToDate: 16_900, waitingCount: 14, waitingAmount: 4_186 },
}

export default function Dashboard() {
  const { data, error, isPending } = useQuery({
    queryKey: ['admin-dashboard'],
    queryFn: () => api.admin.dashboard(),
    refetchInterval: 30_000,
  })
  const charts = useQuery({
    queryKey: ['admin-overview-charts'],
    queryFn: () => api.admin.overviewCharts(),
    refetchInterval: 5 * 60_000,
  })

  return (
    <div className={pageClass()}>
      <PageHeader title="Overview" subtitle="Money worth chasing and how the system is running." />

      {error ? (
        <Alert variant="destructive"><TriangleAlert /><AlertTitle>Couldn’t load the overview</AlertTitle><AlertDescription>{error instanceof Error ? error.message : 'Load failed.'}</AlertDescription></Alert>
      ) : (
        <Skeletonizer loading={!data && isPending}>
          <Body data={data ?? SKELETON} charts={charts.data ?? SKELETON_CHARTS} chartsLoading={charts.isPending} />
        </Skeletonizer>
      )}
    </div>
  )
}

function Body({ data, charts, chartsLoading }: { data: DashboardResponse; charts: OverviewCharts; chartsLoading: boolean }) {
  const stats = withDefaults(data.stats)
  const health = data.health
  const h = charts.headline
  const sameTime = h.paidLastMonthToDate > 0 ? Math.round(((h.paidThisMonth - h.paidLastMonthToDate) / h.paidLastMonthToDate) * 100) : null

  return (
    <div className="space-y-6">
      <Skeletonizer loading={chartsLoading}>
        <StatStrip cols="grid-cols-1 sm:grid-cols-3">
          <Stat label="Paid this month" value={fmtMoney0(h.paidThisMonth)}
            sub={sameTime == null ? 'Nothing last month to compare' : `${sameTime >= 0 ? 'Up' : 'Down'} ${Math.abs(sameTime)}% on last month at this point`} />
          <Stat label="Waiting on a person" value={fmtMoney0(h.waitingAmount)} dot={h.waitingCount > 0 ? 'action' : null}
            sub={h.waitingCount === 0 ? 'Nothing waiting' : `${h.waitingCount.toLocaleString()} paid renewals`} to="/admin/renewal-orders?view=to-fix" />
          <Stat label="Average basket" value={fmtMoney0(stats.avgBasket)} sub={`${stats.renewalsCompleted.toLocaleString()} renewals so far`} />
        </StatStrip>
      </Skeletonizer>

      <Skeletonizer loading={chartsLoading}>
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <OutcomeChart weeks={charts.weeks} waitingAmount={h.waitingAmount} waitingCount={h.waitingCount} />
          <ChannelChart weeks={charts.weeks} />
          <AsicChart days={charts.asic} />
          <AheadChart weeks={charts.ahead} avgBasket={stats.avgBasket} />
        </div>
      </Skeletonizer>

      <Recoverable stats={stats} />

      {health ? <HealthCard health={health} /> : null}
    </div>
  )
}

/* ─────────────────── Stats ─────────────────── */

/** The money worth chasing: the total, then each kind with a way into its list. */
function Recoverable({ stats }: { stats: DashboardResponse['stats'] }) {
  const total = stats.abandonedAtPaymentValue + stats.ontraportPipelineValue + stats.failedRenewalValue + stats.pastCustomersDueSoonValue
  const items = stats.abandonedAtPaymentCount + stats.ontraportPipelineCount + stats.failedRenewalCount + stats.pastCustomersDueSoonCount
  const count = (n: number) => (n === 0 ? 'All clear' : `${n.toLocaleString()} item${n === 1 ? '' : 's'}`)
  return (
    <section className="space-y-3">
      <h2 className="text-heading">Worth chasing</h2>
      <StatStrip cols="grid-cols-2 sm:grid-cols-3 lg:grid-cols-5">
        <Stat className="col-span-2 sm:col-span-1" label="Recoverable revenue" value={total > 0 ? fmtMoney0(total) : '$0'} sub={items === 0 ? 'Nothing to chase' : `across ${items.toLocaleString()} items`} />
        <Stat
          label="Abandoned at payment"
          value={`~${fmtMoney0(stats.abandonedAtPaymentValue)}`}
          sub={count(stats.abandonedAtPaymentCount)}
          to="/admin/leads?outcome=RenewalAvailable"
          hint="Leads found a renewable name but never paid"
        />
        <Stat
          label="Ontraport, due in 30 days"
          value={fmtMoney0(stats.ontraportPipelineValue)}
          sub={count(stats.ontraportPipelineCount)}
          to="/admin/ontraport-sales"
          hint="Paid-in-Ontraport sales that are ready, or nearly ready, to lodge at ASIC"
        />
        <Stat
          label="Failed renewals (30d)"
          value={fmtMoney0(stats.failedRenewalValue)}
          sub={count(stats.failedRenewalCount)}
          dot={stats.failedRenewalCount > 0 ? 'action' : null}
          to="/admin/renewal-orders?view=to-fix"
          hint="Renewals that broke before completion; each is a paying client waiting on a retry"
        />
        <Stat
          label="Past clients due soon"
          value={`~${fmtMoney0(stats.pastCustomersDueSoonValue)}`}
          sub={count(stats.pastCustomersDueSoonCount)}
          to="/admin/renewals"
          hint="1-year renewals from 11–13 months ago, due again in the next 60 days"
        />
      </StatStrip>
    </section>
  )
}

/* ─────────────────── System health ─────────────────── */

/**
 * The Hangfire recurring jobs (registered in Renewtron.Server/Program.cs), in words: a name, what
 * the job does, when it runs, and how long without a run means something's wrong (a little over
 * its schedule). Keep in step with Program.cs when a job is added.
 */
type JobInfo = { label: string; what: string; when: string; staleAfterHours: number }
const jobDisplay: Record<string, JobInfo> = {
  'airwallex-bpay-autopay':     { label: 'BPAY auto-pay', when: 'Every 15 minutes', staleAfterHours: 1,
    what: 'Pays ASIC’s BPAY bills through Airwallex, up to the daily limit set in Settings › Airwallex.' },
  'airwallex-bpay-sync':        { label: 'BPAY payment status', when: 'Every 10 minutes', staleAfterHours: 1,
    what: 'Asks Airwallex about BPAY payments on their way, and marks each bill paid once Airwallex says it is.' },
  'asic-check':                 { label: 'ASIC check of paid renewals', when: 'Daily, 4:00 am', staleAfterHours: 26,
    what: 'Checks paid renewals that aren’t finished against ASIC: renewed elsewhere, lapsed, or still to do.' },
  'asic-due-dates':             { label: 'ASIC due dates', when: 'Daily, 2:00 am', staleAfterHours: 26,
    what: 'Reads each name’s real renewal date from ASIC’s register search, replacing estimated dates.' },
  'asic-key-inbox-scan':        { label: 'ASIC key letters', when: 'Every 15 minutes', staleAfterHours: 1,
    what: 'Reads ASIC key letters from the inbox and saves each key to its client and to Ontraport.' },
  'asic-key-request-run':       { label: 'Key requests', when: 'Twice an hour', staleAfterHours: 2,
    what: 'Closes key requests whose key has arrived, and puts back any a person started but didn’t finish.' },
  'business-names-import':      { label: 'Register import', when: 'Daily, 3:00 am', staleAfterHours: 26,
    what: 'Downloads ASIC’s public business names register when it changes, so customers see their names straight away.' },
  'client-link':                { label: 'Link records to clients', when: 'Every 15 minutes', staleAfterHours: 1,
    what: 'Puts new sales, visits, renewals and key letters on their client, and refreshes clients’ names from the register.' },
  'early-renewal-check':        { label: 'Paid-early check', when: 'Daily, 7:15 am', staleAfterHours: 26,
    what: 'For names paid before ASIC opens their renewal, asks ASIC each morning and lodges the renewal once it’s open.' },
  'email-body-purge':           { label: 'Old email clean-up', when: 'Daily, 3:30 am', staleAfterHours: 26,
    what: 'Clears the text of emails older than 180 days. The record of each email sent stays.' },
  'ontraport-outbox-retry':     { label: 'Ontraport update retry', when: 'Twice an hour', staleAfterHours: 2,
    what: 'Sends renewal results to Ontraport again when saving them there failed the first time.' },
  'ontraport-process-renewals': { label: 'Renew Ontraport sales', when: 'Daily, 7:00 am', staleAfterHours: 26,
    what: 'Starts the ASIC renewal for Ontraport sales that are ready to lodge.' },
  'ontraport-sales-sync':       { label: 'Ontraport sales sync', when: 'Daily, 6:00 am', staleAfterHours: 26,
    what: 'Brings in new sales paid through Ontraport.' },
  'ontraport-search-retry':     { label: 'Retry ASIC name searches', when: 'Every hour', staleAfterHours: 2,
    what: 'Asks ASIC again about Ontraport sales whose name search got no answer, waiting longer between each try.' },
  'payment-follow-up':          { label: 'Payment reminder emails', when: 'Every hour', staleAfterHours: 2,
    what: 'Emails visitors who reached the payment page and left without paying, once, a few hours later.' },
  'registration-asic-check':    { label: 'New names: ASIC fee check', when: 'Every 2 hours', staleAfterHours: 3,
    what: 'For new business name applications paid by BPAY, checks Airwallex and ASIC until ASIC shows the fee as paid.' },
  'renewal-orders-refresh':     { label: 'Paid renewals refresh', when: 'Every 10 minutes', staleAfterHours: 1,
    what: 'Keeps each paid renewal’s status in step with its ASIC runs and payments.' },
  'renewal-reconciliation':     { label: 'Repair stuck renewals', when: 'Daily, 5:30 am', staleAfterHours: 26,
    what: 'Finds renewals that stopped part-way and starts them again, up to 25 a day.' },
}

/** A job missing from the list above still reads as words: "some-new-job" → "Some new job". */
const fallbackJob = (id: string): JobInfo => {
  const words = id.replace(/[-_]+/g, ' ').trim()
  return { label: words.charAt(0).toUpperCase() + words.slice(1), what: 'A background job without a description yet.', when: '', staleAfterHours: 26 }
}

type JobState = 'ok' | 'stale' | 'never'

/** "Is the machine running?": job freshness, queue depth, stuck rows, outbox backlog. */
function HealthCard({ health }: { health: NonNullable<DashboardResponse['health']> }) {
  const jobs = (health.recurringJobs ?? []).map((j) => {
    const display = jobDisplay[j.id] ?? fallbackJob(j.id)
    const state: JobState = !j.lastExecution
      ? 'never'
      : Date.now() - new Date(j.lastExecution).getTime() > display.staleAfterHours * 3600_000
        ? 'stale'
        : 'ok'
    return { ...j, ...display, state }
  }).sort((a, b) => a.label.localeCompare(b.label))
  // "never" doesn't trip the card: right after a deploy every job reads never
  // until its first scheduled run, and that's expected, not an incident.
  const attention = !!health.otpProblem || health.stuckCount > 0 || health.outboxDead > 0 || jobs.some((j) => j.state === 'stale')

  return (
    <section className="space-y-3">
      <h2 className="text-heading">System</h2>
      <Card className="gap-0 overflow-hidden py-0">
        <div className="px-4 py-4 sm:px-5">
          <span className="flex items-center gap-2 text-sm font-medium">
            <span aria-hidden className={cn('size-2 rounded-full', attention ? 'bg-warning' : 'bg-success')} />
            {attention ? 'Something needs a look' : 'All systems running'}
          </span>
        </div>

        {/* OTP: without it no renewal can pay ASIC, so it outranks everything else here */}
        {health.otpProblem ? (
          <div className="border-t px-4 py-4 sm:px-5">
            <Alert variant="destructive" className="border-destructive/30 bg-destructive/5">
              <TriangleAlert />
              <AlertTitle>Renewals are on hold: the OTP SMS can’t be read</AlertTitle>
              <AlertDescription>
                <p title={health.otpProblem}>
                  {health.otpHeldCount > 0
                    ? `${health.otpHeldCount} ${health.otpHeldCount === 1 ? 'renewal is' : 'renewals are'} waiting; they start within an hour of a fix.`
                    : 'New renewals wait until this is fixed.'}{' '}
                  <Link to="/admin/settings" className="font-medium underline">Fix in Settings</Link>
                </p>
                <p className="mt-1 text-caption break-words sm:hidden">{health.otpProblem}</p>
              </AlertDescription>
            </Alert>
          </div>
        ) : null}

        <StatStrip bare cols="grid-cols-3" className="border-t">
          <Stat label="Queue" value={String(health.queueDepth)} sub={health.queueDepth === 1 ? 'job waiting' : 'jobs waiting'} />
          <Stat
            label="Stuck"
            value={String(health.stuckCount)}
            sub={health.stuckCount > 0 ? 'need review' : 'renewals'}
            dot={health.stuckCount > 0 ? 'waiting' : null}
            to={health.stuckCount > 0 ? '/admin/renewals' : undefined}
          />
          <Stat
            label="Outbox"
            value={String(health.outboxPending)}
            sub={health.outboxDead > 0 ? `pending · ${health.outboxDead} dead` : 'pending'}
            dot={health.outboxDead > 0 ? 'waiting' : null}
          />
        </StatStrip>

        {jobs.length > 0 ? (
          <div className="grid grid-cols-2 gap-x-6 gap-y-3 border-t px-4 py-4 sm:grid-cols-3 sm:px-5 lg:grid-cols-5">
            {jobs.map((j) => (
              <div key={j.id} className="flex min-w-0 items-start gap-2">
                <span aria-hidden className={cn('mt-1.5 size-1.5 shrink-0 rounded-full', j.state === 'ok' ? 'bg-success' : j.state === 'stale' ? 'bg-warning' : 'bg-muted-foreground/40')} />
                <div className="min-w-0">
                  <div className="flex min-w-0 items-center gap-0.5">
                    <span className="truncate text-sm">{j.label}</span>
                    <Hint className="-my-1 size-5" text={
                      <div className="space-y-1.5">
                        <p className="font-medium">{j.label}</p>
                        <p>{j.what}</p>
                        <p className="text-caption text-muted-foreground">
                          {[j.when, j.nextExecution ? `next run ${new Date(j.nextExecution).toLocaleString('en-AU', { dateStyle: 'medium', timeStyle: 'short' })}` : null]
                            .filter(Boolean).join(' · ')}
                        </p>
                        {j.state === 'stale' ? <p className="text-caption">It hasn’t run when it should have. Check the Hangfire dashboard.</p> : null}
                      </div>
                    } />
                  </div>
                  <div className="text-caption text-muted-foreground tabular-nums">
                    {j.lastExecution ? relativeTime(j.lastExecution) : 'never ran'}
                  </div>
                </div>
              </div>
            ))}
          </div>
        ) : null}
      </Card>
    </section>
  )
}

function withDefaults(s: Partial<DashboardResponse['stats']>): DashboardResponse['stats'] {
  return {
    totalSearches: s.totalSearches ?? 0,
    successfulSearches: s.successfulSearches ?? 0,
    renewalsInitiated: s.renewalsInitiated ?? 0,
    renewalsCompleted: s.renewalsCompleted ?? 0,
    renewalsPending: s.renewalsPending ?? 0,
    renewalsFailed: s.renewalsFailed ?? 0,
    totalLeads: s.totalLeads ?? 0,
    convertedLeads: s.convertedLeads ?? 0,
    notDueLeads: s.notDueLeads ?? 0,
    renewtronDirectCount: s.renewtronDirectCount ?? 0,
    renewtronDirectRevenue: s.renewtronDirectRevenue ?? 0,
    ontraportCount: s.ontraportCount ?? 0,
    ontraportRevenue: s.ontraportRevenue ?? 0,
    avgBasket: s.avgBasket ?? 79,
    abandonedAtPaymentCount: s.abandonedAtPaymentCount ?? 0,
    abandonedAtPaymentValue: s.abandonedAtPaymentValue ?? 0,
    ontraportPipelineCount: s.ontraportPipelineCount ?? 0,
    ontraportPipelineValue: s.ontraportPipelineValue ?? 0,
    failedRenewalCount: s.failedRenewalCount ?? 0,
    failedRenewalValue: s.failedRenewalValue ?? 0,
    pastCustomersDueSoonCount: s.pastCustomersDueSoonCount ?? 0,
    pastCustomersDueSoonValue: s.pastCustomersDueSoonValue ?? 0,
  }
}
