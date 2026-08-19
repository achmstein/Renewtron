import { Area, AreaChart, ResponsiveContainer, Tooltip } from 'recharts'

/**
 * Tremor-style single-series area trend for KPI tiles: 2px brand line, faint
 * gradient fill, hover tooltip (date + value). One series → no legend; the tile's
 * label names it. Palette validated against the light surface (#0052A4).
 */
export function TrendChart({ data, valueLabel = 'renewals' }: {
  data: Array<{ date?: string; count: number }>
  valueLabel?: string
}) {
  if (data.length === 0) return null
  return (
    <ResponsiveContainer width="100%" height="100%">
      <AreaChart data={data} margin={{ top: 2, right: 2, bottom: 2, left: 2 }}>
        <defs>
          <linearGradient id="trend-fill" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#0052A4" stopOpacity={0.12} />
            <stop offset="100%" stopColor="#0052A4" stopOpacity={0} />
          </linearGradient>
        </defs>
        <Tooltip
          cursor={{ stroke: '#A3C5E6', strokeWidth: 1 }}
          wrapperStyle={{ zIndex: 30, outline: 'none' }}
          content={({ active, payload }) => {
            if (!active || !payload?.length) return null
            const p = payload[0].payload as { date?: string; count: number }
            return (
              <div className="rounded-md border border-zinc-200 bg-white px-2 py-1 text-xs shadow-md">
                {p.date ? <div className="font-mono text-xxs text-zinc-500 tabular-nums">{p.date}</div> : null}
                <div className="font-mono font-medium tabular-nums text-zinc-900">{p.count} {valueLabel}</div>
              </div>
            )
          }}
        />
        <Area
          type="monotone"
          dataKey="count"
          stroke="#0052A4"
          strokeWidth={2}
          fill="url(#trend-fill)"
          dot={false}
          activeDot={{ r: 3.5, fill: '#0052A4', stroke: '#FFFFFF', strokeWidth: 1.5 }}
          isAnimationActive={false}
        />
      </AreaChart>
    </ResponsiveContainer>
  )
}
