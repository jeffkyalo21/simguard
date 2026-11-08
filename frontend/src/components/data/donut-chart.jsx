"use client";

import { Cell, Pie, PieChart, ResponsiveContainer } from "recharts";

// Greys from the chart palette, from light to dark. The legend names every
// slice and its value, so colour is never the only cue.
const SLICE_FILLS = ["var(--chart-1)", "var(--chart-2)", "var(--chart-3)", "var(--chart-4)"];

/**
 * Donut of `slices` ({ key, label, value }). Slices with a value of 0 are not
 * drawn but stay in the legend. `label` is the accessible name of the image;
 * the legend beside it lists each slice with its number.
 * @param {{ slices: { key: string, label: string, value: number }[], label: string,
 *   centre?: React.ReactNode, height?: number }} props
 */
export function DonutChart({ slices, label, centre, height = 180 }) {
  const drawn = slices
    .map((slice, index) => ({ ...slice, fill: SLICE_FILLS[index % SLICE_FILLS.length] }))
    .filter((slice) => slice.value > 0);
  const data = drawn.length > 0 ? drawn : [{ key: "none", label: "", value: 1, fill: "var(--chart-track)" }];

  return (
    <div className="flex flex-col items-center gap-4 sm:flex-row sm:items-center sm:gap-6">
      <div role="img" aria-label={label} className="relative w-full max-w-[180px] shrink-0" style={{ height }}>
        <ResponsiveContainer width="100%" height="100%">
          <PieChart accessibilityLayer={false}>
            <Pie
              data={data}
              dataKey="value"
              nameKey="label"
              innerRadius="62%"
              outerRadius="100%"
              paddingAngle={drawn.length > 1 ? 2 : 0}
              stroke="none"
              isAnimationActive={false}
            >
              {data.map((slice) => (
                <Cell key={slice.key} fill={slice.fill} />
              ))}
            </Pie>
          </PieChart>
        </ResponsiveContainer>
        {centre && <div className="pointer-events-none absolute inset-0 flex items-center justify-center text-center">{centre}</div>}
      </div>
      <ul className="flex flex-col gap-2 text-sm">
        {slices.map((slice, index) => (
          <li key={slice.key} className="flex items-center gap-2">
            <span
              className="size-3 shrink-0 rounded-sm border border-glass-border"
              style={{ background: SLICE_FILLS[index % SLICE_FILLS.length] }}
              aria-hidden="true"
            />
            <span>{slice.label}</span>
            <span className="font-medium tabular-nums">{slice.value}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
