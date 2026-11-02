"use client";

import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";

/**
 * Explains why a control is disabled. Disabled elements get no pointer or
 * focus events, so a wrapper carries the tooltip. Inside menus the wrapper
 * is not focusable (menus own their keyboard model); the item then carries
 * the reason as screen reader text itself.
 * @param {{ reason: string, disabled: boolean, focusable?: boolean,
 *   children: React.ReactNode }} props
 */
export function DisabledReason({ reason, disabled, focusable = true, children }) {
  if (!disabled) return children;

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span tabIndex={focusable ? 0 : undefined} className="flex rounded-lg">
          {children}
        </span>
      </TooltipTrigger>
      <TooltipContent>{reason}</TooltipContent>
    </Tooltip>
  );
}
