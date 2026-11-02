import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { EmptyState } from "@/components/shared/empty-state";

/**
 * Scaffold for app pages until their screens land (#17 to #19).
 * @param {{ title: string, empty: string, icon?: React.ElementType }} props
 */
export function PlaceholderPage({ title, empty, icon }) {
  return (
    <>
      <PageHeader title={title} />
      <GlassCard variant="strong">
        <EmptyState message={empty} icon={icon} />
      </GlassCard>
    </>
  );
}
