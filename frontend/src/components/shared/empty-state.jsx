import { Inbox } from "lucide-react";
import { cn } from "@/lib/utils";

/**
 * One short line and at most one action.
 * @param {{ message: string, icon?: React.ElementType, action?: React.ReactNode,
 *   className?: string }} props
 */
export function EmptyState({ message, icon: Icon = Inbox, action, className }) {
  return (
    <div className={cn("flex flex-col items-center justify-center gap-3 py-16 text-center", className)}>
      <Icon className="size-8 text-muted-foreground" aria-hidden="true" />
      <p className="text-sm text-muted-foreground">{message}</p>
      {action}
    </div>
  );
}
