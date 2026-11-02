import { GlassCard } from "@/components/glass/glass-card";
import { ExplainVisual } from "@/components/landing/feature-visuals";
import { InView } from "@/components/landing/in-view";
import { SAMPLE_SIM } from "@/components/landing/landing-data";
import { SectionHeading } from "@/components/landing/section-heading";
import { RiskBadge } from "@/components/shared/risk-badge";
import { copy } from "@/lib/copy";

const text = copy.landing.why;

/** The explainability card: an alert's top reasons as bars. */
export function WhyItFired() {
  return (
    <InView as="section" aria-labelledby="why-title" className="px-4 pb-24 sm:px-6">
      <div className="mx-auto flex max-w-3xl flex-col gap-10">
        <SectionHeading id="why-title" title={text.title} intro={text.intro} />
        <GlassCard hover className="reveal mx-auto flex w-full max-w-xl flex-col gap-5 p-6 sm:p-8" style={{ "--i": 1 }}>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <span className="text-sm font-semibold tabular-nums">{SAMPLE_SIM.number}</span>
            <RiskBadge level="flagged" />
          </div>
          <ExplainVisual />
        </GlassCard>
      </div>
    </InView>
  );
}
