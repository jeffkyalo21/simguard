import { ShieldAlert, ShieldCheck, ShieldX } from "lucide-react";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

/** @typedef {"cleared" | "escalated" | "flagged"} RiskLevel */

const levels = {
  cleared: {
    Icon: ShieldCheck,
    className: "bg-risk-cleared text-risk-cleared-foreground",
  },
  escalated: {
    Icon: ShieldAlert,
    className: "bg-risk-escalated text-risk-escalated-foreground",
  },
  flagged: {
    Icon: ShieldX,
    className: "bg-risk-flagged text-risk-flagged-foreground",
  },
};

/**
 * Risk level as icon plus text, so colour is never the only signal.
 * @param {{ level: RiskLevel, className?: string }} props
 */
export function RiskBadge({ level, className }) {
  const { Icon, className: levelClass } = levels[level] ?? levels.escalated;

  return (
    <span
      data-level={level}
      className={cn(
        "inline-flex h-6 items-center gap-1 rounded-full px-2.5 text-xs font-medium",
        levelClass,
        className
      )}
    >
      <Icon className="size-3.5" aria-hidden="true" />
      {copy.risk[level] ?? copy.risk.escalated}
    </span>
  );
}
