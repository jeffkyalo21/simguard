import { Skeleton } from "@/components/ui/skeleton";

/** Placeholder sized like a chart, so nothing jumps when data arrives. */
export function ChartSkeleton({ height = 220 }) {
  return <Skeleton className="w-full rounded-lg" style={{ height }} />;
}

/** Placeholder rows for lists, bar lists and detail lists. */
export function ListSkeleton({ rows = 4 }) {
  return (
    <div className="flex flex-col gap-4">
      {Array.from({ length: rows }, (_, index) => (
        <Skeleton key={index} className="h-5 w-full" />
      ))}
    </div>
  );
}
