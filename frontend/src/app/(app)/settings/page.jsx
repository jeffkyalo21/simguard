import { Suspense } from "react";
import { SettingsView } from "@/components/settings/settings-view";
import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { Skeleton } from "@/components/ui/skeleton";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.settings.title };

// The view reads the selected tab from the URL, so it renders on the client.
function SettingsFallback() {
  return (
    <div role="status" aria-busy="true" className="flex flex-col gap-6">
      <span className="sr-only">{copy.a11y.loading}</span>
      <PageHeader title={copy.pages.settings.title} as="p" />
      <Skeleton className="h-9 w-full max-w-md" />
      <GlassCard variant="strong" className="flex flex-col gap-4 p-5">
        {Array.from({ length: 3 }, (_, index) => (
          <Skeleton key={index} className="h-6 w-full" />
        ))}
      </GlassCard>
    </div>
  );
}

export default function SettingsPage() {
  return (
    <Suspense fallback={<SettingsFallback />}>
      <SettingsView />
    </Suspense>
  );
}
