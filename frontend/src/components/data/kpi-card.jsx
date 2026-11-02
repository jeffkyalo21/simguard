import { GlassCard } from "@/components/glass/glass-card";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * One number with its label. `hero` marks the one KPI per page that gets
 * the accent glow; values stay ink.
 * @param {{ label: string, value?: React.ReactNode, hero?: boolean,
 *   loading?: boolean, icon?: React.ElementType }} props
 */
export function KpiCard({ label, value, hero = false, loading = false, icon: Icon }) {
  return (
    <GlassCard hover hero={hero} className="flex h-full flex-col gap-2">
      <p className="flex items-center gap-1.5 text-sm text-muted-foreground">
        {Icon && <Icon className="size-4" aria-hidden="true" />}
        {label}
      </p>
      {loading ? (
        <Skeleton className="h-9 w-28" />
      ) : (
        <p className="text-3xl font-semibold tracking-tight tabular-nums">
          {value}
        </p>
      )}
    </GlassCard>
  );
}
