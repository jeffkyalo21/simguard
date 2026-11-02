import { Activity, ChartLine, Gauge, Library, Users } from "lucide-react";
import { GlassCard } from "@/components/glass/glass-card";
import { InView } from "@/components/landing/in-view";
import { SectionHeading } from "@/components/landing/section-heading";
import { StepsProgress } from "@/components/landing/steps-progress";
import { copy } from "@/lib/copy";

const text = copy.landing.detection;
const icons = { behaviour: Activity, peers: Users, triage: Gauge, cases: Library };

/**
 * The four detection steps with a line that fills as the page scrolls.
 * Model names stay in small muted text under each plain sentence.
 */
export function HowItWorks() {
  return (
    <InView as="section" id="how-it-works" aria-labelledby="how-title" threshold={0.1} className="px-4 pb-24 sm:px-6">
      <div className="mx-auto flex max-w-3xl flex-col gap-10">
        <SectionHeading id="how-title" title={text.title} intro={text.intro} />
        <StepsProgress>
          <div className="absolute top-5 bottom-5 left-5 w-0.5 rounded-full bg-chart-track" aria-hidden="true">
            <div data-steps-line className="steps-line h-full w-full rounded-full bg-primary" />
          </div>
          <ol className="flex flex-col gap-6">
            {text.steps.map(({ key, title, text: body, model }, index) => {
              const Icon = icons[key];

              return (
                <li key={key} data-step className="step relative flex items-start gap-4 sm:gap-5">
                  <span
                    className="step-dot relative z-10 flex size-10 shrink-0 items-center justify-center rounded-full border border-glass-border bg-glass-strong text-sm font-semibold"
                    aria-hidden="true"
                  >
                    {index + 1}
                  </span>
                  <GlassCard variant="strong" className="step-card flex min-w-0 flex-1 items-start gap-4 p-5">
                    <Icon className="mt-1 size-5 shrink-0 text-muted-foreground" aria-hidden="true" />
                    <div className="flex min-w-0 flex-col gap-1">
                      <h3 className="text-lg font-semibold">{title}</h3>
                      <p className="text-muted-foreground">{body}</p>
                      <p className="text-xs text-muted-foreground">{model}</p>
                    </div>
                  </GlassCard>
                </li>
              );
            })}
          </ol>
        </StepsProgress>
        <aside className="reveal ml-14 flex items-center gap-3 rounded-2xl border border-dashed border-input px-4 py-3 sm:ml-15">
          <ChartLine className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          <p className="text-sm">
            {text.note} <span className="text-xs text-muted-foreground">{text.noteModel}</span>
          </p>
        </aside>
      </div>
    </InView>
  );
}
