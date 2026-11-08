import { Suspense } from "react";
import { GlassCard } from "@/components/glass/glass-card";
import { ImportsView } from "@/components/imports/imports-view";
import { PageHeader } from "@/components/layout/page-header";
import { Skeleton } from "@/components/ui/skeleton";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.imports.title };

// The view reads the type and tab from the URL, so it renders on the client.
function ImportsFallback() {
  return (
    <div role="status" aria-busy="true" className="flex flex-col gap-6">
      <span className="sr-only">{copy.a11y.loading}</span>
      <PageHeader title={copy.pages.imports.title} as="p" />
      <Skeleton className="h-9 w-full max-w-xs" />
      <GlassCard variant="strong" className="flex flex-col gap-4 p-5">
        {Array.from({ length: 3 }, (_, index) => (
          <Skeleton key={index} className="h-6 w-full" />
        ))}
      </GlassCard>
    </div>
  );
}

export default function ImportsPage() {
  return (
    <Suspense fallback={<ImportsFallback />}>
      <ImportsView />
    </Suspense>
  );
}
