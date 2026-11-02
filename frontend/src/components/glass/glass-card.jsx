import { cn } from "@/lib/utils";

/**
 * Glass card in one of three materials:
 * - "default": glanceable content (KPIs, charts, short lists)
 * - "strong": tables, forms and long text
 * - "dark": the one emphasis card per page (white text)
 * `hover` lifts the card slightly; `hero` adds the accent glow (one hero
 * KPI per page).
 * @param {{ variant?: "default" | "strong" | "dark", hover?: boolean,
 *   hero?: boolean, as?: React.ElementType, className?: string }
 *   & Record<string, unknown>} props
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
        variant === "dark" && "glass-card-dark",
        hover && "glass-card-hover",
        hero && "glass-card-hero",
        className
      )}
      {...props}
    />
  );
}
