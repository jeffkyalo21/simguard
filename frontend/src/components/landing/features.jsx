import { ArrowLeftRight, ScanSearch, ShieldAlert } from "lucide-react";
import { GlassCard } from "@/components/glass/glass-card";
import { AllocateVisual, DetectVisual, ExplainVisual } from "@/components/landing/feature-visuals";
import { InView } from "@/components/landing/in-view";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const rows = {
  detect: { Icon: ShieldAlert, Visual: DetectVisual },
  allocate: { Icon: ArrowLeftRight, Visual: AllocateVisual },
  explain: { Icon: ScanSearch, Visual: ExplainVisual },
};

/** Detect, Allocate, Explain: alternating rows that reveal on scroll. */
export function Features() {
  return (
    <section id="features" aria-labelledby="features-title" className="px-4 pb-24 sm:px-6">
      <div className="mx-auto flex max-w-5xl flex-col gap-10">
        <h2 id="features-title" className="text-center text-3xl font-semibold tracking-tight sm:text-4xl">
          {copy.landing.featuresTitle}
        </h2>
        {copy.landing.features.map(({ key, title, text }, index) => {
          const { Icon, Visual } = rows[key];
          const flip = index % 2 === 1;

          return (
            <InView key={key} className="grid items-center gap-4 md:grid-cols-2 md:gap-8">
              <GlassCard
                variant="strong"
                hover
                className={cn("reveal glow-hover flex flex-col gap-3 p-6 sm:p-8", flip && "md:order-2")}
                style={{ "--i": 0 }}
              >
                <span className="flex size-10 items-center justify-center rounded-xl bg-brand-soft text-brand-text">
                  <Icon className="size-5" aria-hidden="true" />
                </span>
                <h3 className="text-2xl font-semibold tracking-tight">{title}</h3>
                <p className="text-lg text-muted-foreground">{text}</p>
              </GlassCard>
              <GlassCard className="reveal p-6 sm:p-8" style={{ "--i": 1 }} aria-hidden="true">
                <Visual />
              </GlassCard>
            </InView>
          );
        })}
      </div>
    </section>
  );
}
