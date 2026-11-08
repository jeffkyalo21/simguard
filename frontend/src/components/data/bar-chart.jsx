"use client";

import {
  Bar,
  BarChart as RechartsBarChart,
  CartesianGrid,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

const axisTick = { fill: "var(--chart-axis)", fontSize: 12 };

function ChartTooltip({ active, payload, label, formatLabel, formatValue }) {
  // A day without a value (for example a future day of the cycle) has no tooltip.
  if (!active || !payload?.length || payload[0].value == null) return null;

  return (
    <div className="rounded-lg border border-glass-border bg-popover px-3 py-2 text-xs shadow-md">
      <p className="text-muted-foreground">{formatLabel(label)}</p>
      <p className="font-medium tabular-nums">{formatValue(payload[0].value)}</p>
    </div>
  );
}

/**
 * Theme-aware bar chart, one bar per row of `data`: transparent background,
 * soft gridlines, no draw-in animation (motion rules). Colours come from CSS
 * variables, so light and dark themes just work. A row whose value is null
 * leaves a gap, so a short series can still span a fixed axis.
 * @param {{ data: object[], xKey: string, yKey: string, label: string,
 *   xTicks?: (string | number)[], formatX?: (v: any) => string,
 *   formatY?: (v: number) => string, yDomain?: [number | string, number | string],
 *   limit?: { y: number, label: string }, limits?: { y: number, label: string }[],
 *   height?: number }} props
 */
export function BarChart({
  data,
  xKey,
  yKey,
  label,
  xTicks,
  formatX = String,
  formatY = String,
  yDomain = [0, "auto"],
  limit,
  limits = [],
  height = 220,
}) {
  return (
    <div role="img" aria-label={label} className="w-full" style={{ height }}>
      <ResponsiveContainer width="100%" height="100%">
        {/* The wrapper is the labelled image, so the svg is not a tab stop. */}
        <RechartsBarChart
          data={data}
          margin={{ top: 16, right: 12, bottom: 0, left: 0 }}
          barCategoryGap="20%"
          accessibilityLayer={false}
        >
          <CartesianGrid vertical={false} stroke="var(--chart-grid)" />
          <XAxis
            dataKey={xKey}
            ticks={xTicks}
            interval="preserveStartEnd"
            tickFormatter={formatX}
            tick={axisTick}
            axisLine={false}
            tickLine={false}
            tickMargin={8}
          />
          <YAxis
            domain={yDomain}
            tickFormatter={formatY}
            tick={axisTick}
            axisLine={false}
            tickLine={false}
            width={52}
          />
          <Tooltip
            cursor={{ fill: "var(--chart-grid)" }}
            isAnimationActive={false}
            content={<ChartTooltip formatLabel={formatX} formatValue={formatY} />}
          />
          <Bar
            dataKey={yKey}
            fill="var(--foreground)"
            radius={[2, 2, 0, 0]}
            maxBarSize={28}
            isAnimationActive={false}
          />
          {limit && (
            <ReferenceLine
              y={limit.y}
              stroke="var(--chart-axis)"
              strokeDasharray="4 4"
              label={{ value: limit.label, position: "insideTopRight", fill: "var(--chart-axis)", fontSize: 12 }}
            />
          )}
          {limits.map((item) => (
            <ReferenceLine
              key={`${item.y}-${item.label}`}
              y={item.y}
              stroke="var(--chart-axis)"
              strokeDasharray="4 4"
              label={{ value: item.label, position: "insideTopRight", fill: "var(--chart-axis)", fontSize: 12 }}
            />
          ))}
        </RechartsBarChart>
      </ResponsiveContainer>
    </div>
  );
}
