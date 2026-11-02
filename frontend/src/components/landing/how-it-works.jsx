import { CheckCheck, Gauge, Plug } from "lucide-react";
import { GlassCard } from "@/components/glass/glass-card";
import { StepsProgress } from "@/components/landing/steps-progress";
import { copy } from "@/lib/copy";

const icons = { connect: Plug, score: Gauge, act: CheckCheck };

/** Connect, Score, Act with a line that fills as the page scrolls. */
export function HowItWorks() {
  return (
    <section id="how" aria-labelledby="how-title" className="px-4 pb-24 sm:px-6">
      <div className="mx-auto flex max-w-3xl flex-col gap-10">
        <h2 id="how-title" className="text-center text-3xl font-semibold tracking-tight sm:text-4xl">
          {copy.landing.howTitle}
        </h2>
        <StepsProgress>
          <div className="absolute top-5 bottom-5 left-5 w-0.5 rounded-full bg-foreground/10" aria-hidden="true">
            <div data-steps-line className="steps-line h-full w-full rounded-full bg-brand" />
          </div>
          <ol className="flex flex-col gap-6">
            {copy.landing.steps.map(({ key, title, text }, index) => {
              const Icon = icons[key];

              return (
                <li key={key} data-step className="step relative flex items-start gap-5">
                  <span
                    className="step-dot relative z-10 flex size-10 shrink-0 items-center justify-center rounded-full border border-glass-border bg-glass-strong text-sm font-semibold"
                    aria-hidden="true"
                  >
                    {index + 1}
                  </span>
                  <GlassCard variant="strong" className="step-card flex flex-1 items-center gap-4 p-5">
                    <Icon className="size-5 shrink-0 text-muted-foreground" aria-hidden="true" />
                    <div className="flex flex-col gap-0.5">
                      <h3 className="text-lg font-semibold">{title}</h3>
                      <p className="text-muted-foreground">{text}</p>
                    </div>
                  </GlassCard>
                </li>
              );
            })}
          </ol>
        </StepsProgress>
      </div>
    </section>
  );
}
