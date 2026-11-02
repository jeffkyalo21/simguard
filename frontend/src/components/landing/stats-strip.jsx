import { GlassCard } from "@/components/glass/glass-card";
import { CountUp } from "@/components/landing/count-up";
import { InView } from "@/components/landing/in-view";
import { copy } from "@/lib/copy";

/** Three counters that count up once on first view. */
export function StatsStrip() {
  return (
    <section className="px-4 pb-20 sm:px-6">
      <InView className="mx-auto max-w-5xl">
        <GlassCard variant="strong" className="reveal grid gap-6 px-6 py-8 sm:grid-cols-3 sm:divide-x sm:divide-border">
          {copy.landing.stats.map(({ key, label, value }) => (
            <div key={key} className="flex flex-col items-center gap-1 text-center">
              <span className="text-4xl font-semibold tracking-tight sm:text-5xl">
                <CountUp value={value} />
              </span>
              <span className="text-sm text-muted-foreground">{label}</span>
            </div>
          ))}
        </GlassCard>
      </InView>
    </section>
  );
}
