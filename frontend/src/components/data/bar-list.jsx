import { ProgressBar } from "@/components/data/progress-bar";
import { formatNumber } from "@/lib/format";

/**
 * Labelled horizontal bars with values, scaled to the largest value.
 * @param {{ items: { key: string, label: string, value: number }[] }} props
 */
export function BarList({ items }) {
  const max = Math.max(...items.map((item) => item.value), 1);

  return (
    <ul className="flex flex-col gap-4">
      {items.map(({ key, label, value }) => (
        <li key={key} className="grid grid-cols-[minmax(0,7.5rem)_1fr_2.5rem] items-center gap-3 text-sm">
          <span className="truncate">{label}</span>
          <ProgressBar value={value} max={max} label={label} valueText={formatNumber(value)} />
          <span className="text-right tabular-nums">{formatNumber(value)}</span>
        </li>
      ))}
    </ul>
  );
}
