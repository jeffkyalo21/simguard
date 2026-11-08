import { Ban, CircleDashed, UserCheck } from "lucide-react";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

/** @typedef {"assigned" | "available" | "inactive"} SimStatus */

// Neutral tones: status is not risk, so it never borrows risk colours.
const statuses = {
  assigned: { Icon: UserCheck, className: "bg-foreground/8 text-foreground" },
  available: { Icon: CircleDashed, className: "border border-input text-foreground" },
  inactive: { Icon: Ban, className: "border border-dashed border-input text-muted-foreground" },
};

/**
 * SIM status as icon plus text. With `hint`, the badge is focusable and a
 * tooltip explains it (used where the status is read only).
 * @param {{ status: SimStatus, className?: string, hint?: string }} props
 */
export function StatusBadge({ status, className, hint }) {
  const { Icon, className: statusClass } = statuses[status] ?? statuses.available;

  const badge = (
    <span
      tabIndex={hint ? 0 : undefined}
      data-status={status}
      className={cn(
        "inline-flex h-6 items-center gap-1 rounded-full px-2.5 text-xs font-medium whitespace-nowrap",
        statusClass,
        className
      )}
    >
      <Icon className="size-3.5" aria-hidden="true" />
      {copy.simStatus[status] ?? status}
    </span>
  );

  if (!hint) return badge;

  return (
    <Tooltip>
      <TooltipTrigger asChild>{badge}</TooltipTrigger>
      <TooltipContent>{hint}</TooltipContent>
    </Tooltip>
  );
}
