import { Stat, StatStrip } from '../_ui'
import { durationShort, fmtMoney0 } from '../_utils'
import type { Stats } from './model'

export function StatsStrip({ stats }: { stats: Stats }) {
  const attention = stats.stuckValue + stats.needsReviewValue
  return (
    <StatStrip>
      <Stat
        label="Success rate"
        period="Last 30 days"
        value={`${stats.successRate30d}%`}
        sub={`${stats.completed30d.toLocaleString()} of ${stats.decided30d.toLocaleString()} finished`}
        dot={stats.decided30d === 0 ? null : stats.successRate30d < 70 ? 'action' : stats.successRate30d < 90 ? 'waiting' : null}
      />
      <Stat
        label="Revenue"
        period="This month"
        value={fmtMoney0(stats.revenueMtd)}
        sub={stats.avgCompletionMinutes != null ? `${durationShort(Number(stats.avgCompletionMinutes) / 60)} to renew, on average` : undefined}
      />
      <Stat
        label="Needs attention"
        value={fmtMoney0(attention)}
        sub={`${fmtMoney0(stats.liveValue)} under way · ${fmtMoney0(stats.scheduledRetryValue)} retrying`}
        dot={attention > 0 ? 'waiting' : null}
        hint="Stuck or failed renewals waiting on a person"
      />
      <Stat
        label="Today"
        period={stats.deltaPct != null ? `${stats.deltaPct >= 0 ? '+' : ''}${stats.deltaPct}%` : undefined}
        value={stats.today.toLocaleString()}
        sub={`${stats.yesterday.toLocaleString()} yesterday`}
        trend={stats.daily14d.map((d) => ({ date: d.date, count: d.count }))}
        hint="Renewals started today; the line is the last 14 days"
      />
    </StatStrip>
  )
}
