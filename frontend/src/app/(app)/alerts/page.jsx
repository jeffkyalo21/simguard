import { Suspense } from "react";
import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { AlertsView } from "@/components/alerts/alerts-view";
import { Skeleton } from "@/components/ui/skeleton";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.alerts.title };

function AlertsFallback() {
  return (
    <div role="status" aria-busy="true" className="flex flex-col gap-6">
      <span className="sr-only">{copy.a11y.loading}</span>
      <PageHeader title={copy.pages.alerts.title} as="p" />
      <div className="flex gap-2"><Skeleton className="h-10 w-32" /><Skeleton className="h-10 w-32" /></div>
      <GlassCard variant="strong" className="flex flex-col gap-4 p-5">
        {Array.from({ length: 8 }, (_, index) => <Skeleton key={index} className="h-6 w-full" />)}
      </GlassCard>
    </div>
  );
}

export default function AlertsPage() {
  return <Suspense fallback={<AlertsFallback />}><AlertsView /></Suspense>;
}
