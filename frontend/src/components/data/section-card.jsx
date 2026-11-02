import { GlassCard } from "@/components/glass/glass-card";
import { cn } from "@/lib/utils";

/**
 * A titled card: title left, optional action right, then content.
 * `variant` picks the glass material (see GlassCard); default cards lift on
 * hover. `flush` drops the body padding for tables that run edge to edge.
 * @param {{ title: string, action?: React.ReactNode,
 *   variant?: "default" | "strong" | "dark", flush?: boolean,
 *   className?: string, children: React.ReactNode }} props
 */
export function SectionCard({ title, action, variant = "default", flush = false, className, children }) {
  return (
    <GlassCard
      as="section"
      variant={variant}
      hover={variant !== "strong"}
      className={cn("flex h-full min-w-0 flex-col gap-4", flush && "p-0", className)}
    >
      <div className={cn("flex min-h-10 items-center justify-between gap-3", flush && "px-5 pt-5")}>
        <h2 className="text-base font-semibold">{title}</h2>
        {action}
      </div>
      <div className="flex min-h-0 flex-1 flex-col">{children}</div>
    </GlassCard>
  );
}
