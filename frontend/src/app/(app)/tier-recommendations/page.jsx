import { Suspense } from "react";
import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { TierRecommendationsView } from "@/components/tier-recommendations/tier-recommendations-view";
import { Skeleton } from "@/components/ui/skeleton";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.tierRecommendations.title };

// The list reads its filter from the URL, so it renders on the client.
function TierRecommendationsFallback() {
  return (
    <div role="status" aria-busy="true" className="flex flex-col gap-6">
      <span className="sr-only">{copy.a11y.loading}</span>
      <PageHeader title={copy.pages.tierRecommendations.title} as="p" />
      <Skeleton className="h-20 w-full" />
      <GlassCard variant="strong" className="flex flex-col gap-4 p-5">
        {Array.from({ length: 6 }, (_, index) => (
          <Skeleton key={index} className="h-6 w-full" />
        ))}
      </GlassCard>
    </div>
  );
}

export default function TierRecommendationsPage() {
  return (
    <Suspense fallback={<TierRecommendationsFallback />}>
      <TierRecommendationsView />
    </Suspense>
  );
}
