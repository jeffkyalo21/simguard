import { Skeleton } from "@/components/ui/skeleton";
import { copy } from "@/lib/copy";

// Visible status while a route streams in.
export default function Loading() {
  return (
    <div role="status" aria-busy="true" className="flex min-h-dvh flex-col gap-6 p-8">
      <span className="sr-only">{copy.a11y.loading}</span>
      <Skeleton className="h-8 w-48" />
      <Skeleton className="h-64 w-full rounded-xl" />
    </div>
  );
}
