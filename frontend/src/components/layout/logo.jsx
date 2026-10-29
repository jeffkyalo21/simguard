import { ShieldCheck } from "lucide-react";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

/**
 * 24px rounded-square mark plus the product name.
 * @param {{ showName?: boolean, className?: string }} props
 */
export function Logo({ showName = true, className }) {
  return (
    <span className={cn("flex items-center gap-2", className)}>
      <span
        aria-hidden="true"
        className="flex size-6 shrink-0 items-center justify-center rounded-md bg-primary text-primary-foreground"
      >
        <ShieldCheck className="size-4" />
      </span>
      <span className={cn("text-base font-semibold", !showName && "sr-only")}>
        {copy.app.name}
      </span>
    </span>
  );
}
