import { ProgressBar } from "@/components/data/progress-bar";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

/** Risk score (0 to 1) to a level; the bar tone follows the level. */
export function riskLevel(score) {
  if (score >= 0.8) return "flagged";
  if (score >= 0.5) return "escalated";
  return "cleared";
}

/**
 * Risk bar plus percentage.
 * @param {{ score: number, className?: string }} props
 */
export function RiskBar({ score, className }) {
  const percent = Math.round(score * 100);
  const text = copy.units.percent(percent);

  return (
    <div className={cn("flex min-w-32 items-center gap-2", className)}>
      <ProgressBar
        value={percent}
        label={copy.columns.risk}
        valueText={text}
        tone={riskLevel(score)}
        className="w-16"
      />
      <span className="w-9 text-sm tabular-nums">{text}</span>
    </div>
  );
}
