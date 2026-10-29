import { cn } from "@/lib/utils";

/**
 * Frosted card for the app shell. Use "strong" for tables, forms, long text
 * and the alerts queue; thin glass is for light, glanceable content only.
 * Only works inside .glass-app, where the glass tokens are defined.
 * @param {{ variant?: "default" | "strong", as?: React.ElementType,
 *   className?: string } & Record<string, unknown>} props
 */
export function GlassCard({ variant = "default", as: Component = "div", className, ...props }) {
  return (
    <Component
      data-slot="glass-card"
      data-variant={variant}
      className={cn("glass-card p-5", variant === "strong" && "glass-card-strong", className)}
      {...props}
    />
  );
}
