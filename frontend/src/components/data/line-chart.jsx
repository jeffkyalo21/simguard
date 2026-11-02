"use client";

import {
  CartesianGrid,
  Line,
  LineChart as RechartsLineChart,
  ReferenceDot,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

const axisTick = { fill: "var(--chart-axis)", fontSize: 12 };

function ChartTooltip({ active, payload, label, formatLabel, formatValue }) {
  if (!active || !payload?.length) return null;

  return (
    <div className="rounded-lg border border-glass-border bg-popover px-3 py-2 text-xs shadow-md">
      <p className="text-muted-foreground">{formatLabel(label)}</p>
      <p className="font-medium tabular-nums">{formatValue(payload[0].value)}</p>
    </div>
  );
}

/**
 * Theme-aware line chart: transparent background, soft gridlines, no
 * draw-in animation (motion rules). Colours come from CSS variables, so
 * light and dark themes just work.
 * @param {{ data: object[], xKey: string, yKey: string, label: string,
 *   xTicks?: (string | number)[], xType?: "category" | "number",
 *   xDomain?: [number, number], formatX?: (v: any) => string,
 *   formatY?: (v: number) => string, yDomain?: [number | string, number | string],
 *   limit?: { y: number, label: string }, peak?: { x: any, y: number, label: string },
 *   height?: number }} props
 */
export function LineChart({
  data,
  xKey,
  yKey,
  label,
  xTicks,
  xType = "category",
  xDomain,
  formatX = String,
  formatY = String,
  yDomain = [0, "auto"],
  limit,
  peak,
  height = 220,
}) {
  return (
    <div role="img" aria-label={label} className="w-full" style={{ height }}>
      <ResponsiveContainer width="100%" height="100%">
        {/* The wrapper is the labelled image, so the svg is not a tab stop. */}
        <RechartsLineChart
          data={data}
          margin={{ top: 16, right: 12, bottom: 0, left: 0 }}
          accessibilityLayer={false}
        >
          <CartesianGrid vertical={false} stroke="var(--chart-grid)" />
          <XAxis
            dataKey={xKey}
            type={xType}
            domain={xDomain}
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
            cursor={{ stroke: "var(--chart-grid)" }}
            isAnimationActive={false}
            content={<ChartTooltip formatLabel={formatX} formatValue={formatY} />}
          />
          {limit && (
            <ReferenceLine
              y={limit.y}
              stroke="var(--chart-axis)"
              strokeDasharray="4 4"
              label={{ value: limit.label, position: "insideTopRight", fill: "var(--chart-axis)", fontSize: 12 }}
            />
          )}
          <Line
            type="monotone"
            dataKey={yKey}
            stroke="var(--foreground)"
            strokeWidth={2}
            dot={false}
            activeDot={{ r: 4, fill: "var(--foreground)", stroke: "var(--background)" }}
            isAnimationActive={false}
          />
          {peak && (
            <ReferenceDot
              x={peak.x}
              y={peak.y}
              r={4}
              fill="var(--foreground)"
              stroke="var(--background)"
              label={{ value: peak.label, position: "top", fill: "var(--chart-axis)", fontSize: 12 }}
            />
          )}
        </RechartsLineChart>
      </ResponsiveContainer>
    </div>
  );
}
