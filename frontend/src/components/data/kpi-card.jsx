import { GlassCard } from "@/components/glass/glass-card";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";

/**
 * One number with its label. `hero` marks the one KPI per page that gets
 * the accent glow and accent value.
 * @param {{ label: string, value?: React.ReactNode, hero?: boolean,
 *   loading?: boolean, icon?: React.ElementType }} props
 */
export function KpiCard({ label, value, hero = false, loading = false, icon: Icon }) {
  return (
    <GlassCard hover hero={hero} className="flex h-full flex-col gap-2 px-5 py-4">
      <p className="flex items-center gap-1.5 text-sm text-muted-foreground">
        {Icon && <Icon className="size-4" aria-hidden="true" />}
        {label}
      </p>
      {loading ? (
        <Skeleton className="h-9 w-28" />
      ) : (
        <p className={cn("text-3xl font-semibold tracking-tight tabular-nums", hero && "text-brand-text")}>
          {value}
        </p>
      )}
    </GlassCard>
  );
}
