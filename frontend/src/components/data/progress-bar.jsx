import { cn } from "@/lib/utils";

const tones = {
  ink: "bg-foreground",
  flagged: "bg-risk-flagged-strong",
  escalated: "bg-risk-escalated-strong",
  cleared: "bg-risk-cleared-strong",
};

/**
 * Horizontal bar with an accessible value. The visible text sits beside it,
 * so the bar is never the only signal.
 * @param {{ value: number, max?: number, label: string, valueText?: string,
 *   tone?: keyof typeof tones, className?: string }} props
 */
export function ProgressBar({ value, max = 100, label, valueText, tone = "ink", className }) {
  const percent = max > 0 ? Math.min(Math.max(value / max, 0), 1) * 100 : 0;

  return (
    <div
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={max}
      aria-valuenow={Math.min(value, max)}
      aria-valuetext={valueText}
      className={cn("h-1.5 w-full overflow-hidden rounded-full bg-foreground/10", className)}
    >
      <div className={cn("h-full rounded-full", tones[tone])} style={{ width: `${percent}%` }} />
    </div>
  );
}
