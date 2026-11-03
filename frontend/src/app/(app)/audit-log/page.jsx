import { Suspense } from "react";
import { AuditLogView } from "@/components/audit/audit-log-view";
import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { Skeleton } from "@/components/ui/skeleton";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.auditLog.title };

// The list reads its filters from the URL, so it renders on the client.
function AuditLogFallback() {
  return (
    <div role="status" aria-busy="true" className="flex flex-col gap-6">
      <span className="sr-only">{copy.a11y.loading}</span>
      <PageHeader title={copy.pages.auditLog.title} as="p" />
      <Skeleton className="h-10 w-full max-w-xl" />
      <GlassCard variant="strong" className="flex flex-col gap-4 p-5">
        {Array.from({ length: 8 }, (_, index) => (
          <Skeleton key={index} className="h-6 w-full" />
        ))}
      </GlassCard>
    </div>
  );
}

export default function AuditLogPage() {
  return (
    <Suspense fallback={<AuditLogFallback />}>
      <AuditLogView />
    </Suspense>
  );
}
