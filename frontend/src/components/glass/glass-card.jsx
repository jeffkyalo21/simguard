import { cn } from "@/lib/utils";

/**
 * Frosted card. Use "strong" for tables, forms, long text and the alerts
 * queue; thin glass is for light, glanceable content only. `hover` lifts the
 * card slightly; `hero` adds the accent glow (one hero KPI per page).
 * @param {{ variant?: "default" | "strong", hover?: boolean, hero?: boolean,
 *   as?: React.ElementType, className?: string } & Record<string, unknown>} props
 */
export function GlassCard({
  variant = "default",
  hover = false,
  hero = false,
  as: Component = "div",
  className,
  ...props
}) {
  return (
    <Component
      data-slot="glass-card"
      data-variant={variant}
      className={cn(
        "glass-card",
        variant === "strong" && "glass-card-strong",
        hover && "glass-card-hover",
        hero && "glass-card-hero",
        className
      )}
      {...props}
    />
  );
}
