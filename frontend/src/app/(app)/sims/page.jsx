import { Suspense } from "react";
import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { SimsView } from "@/components/sims/sims-view";
import { Skeleton } from "@/components/ui/skeleton";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.sims.title };

// The list reads its filters from the URL, so it renders on the client;
// the prerendered shell shows a skeleton of the same layout.
function SimsFallback() {
  return (
    <div role="status" aria-busy="true" className="flex flex-col gap-6">
      <span className="sr-only">{copy.a11y.loading}</span>
      <PageHeader title={copy.pages.sims.title} as="p" />
      <Skeleton className="h-10 w-full max-w-xl" />
      <GlassCard variant="strong" className="flex flex-col gap-4 p-5">
        {Array.from({ length: 8 }, (_, index) => (
          <Skeleton key={index} className="h-6 w-full" />
        ))}
      </GlassCard>
    </div>
  );
}

export default function SimsPage() {
  return (
    <Suspense fallback={<SimsFallback />}>
      <SimsView />
    </Suspense>
  );
}
