import { Skeleton } from "@/components/ui/skeleton";
import { copy } from "@/lib/copy";

/** Placeholder while a sign-in form reads its URL parameters. */
export function FormFallback({ rows = 2 }) {
  return (
    <div role="status" aria-busy="true" className="flex flex-col gap-4">
      <span className="sr-only">{copy.a11y.loading}</span>
      {Array.from({ length: rows }, (_, index) => (
        <Skeleton key={index} className="h-16 w-full" />
      ))}
      <Skeleton className="h-10 w-full" />
    </div>
  );
}
