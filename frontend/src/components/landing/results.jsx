import { FlaskConical } from "lucide-react";
import { GlassCard } from "@/components/glass/glass-card";
import { CountUp } from "@/components/landing/count-up";
import { InView } from "@/components/landing/in-view";
import { RESULTS } from "@/components/landing/landing-data";
import { SectionHeading } from "@/components/landing/section-heading";
import { copy } from "@/lib/copy";

const text = copy.landing.results;

const values = {
  precision: <CountUp value={RESULTS.precision} suffix="%" />,
  recall: (
    <>
      <CountUp value={RESULTS.recallLow} /> {text.rangeJoin} <CountUp value={RESULTS.recallHigh} suffix="%" />
    </>
  ),
  types: <CountUp value={RESULTS.types} />,
};

/** Test results as count-up tiles, with the synthetic data note in view. */
export function Results() {
  return (
    <InView as="section" id="results" aria-labelledby="results-title" threshold={0.1} className="px-4 pb-24 sm:px-6">
      <div className="mx-auto flex max-w-5xl flex-col gap-10">
        <SectionHeading id="results-title" title={text.title} intro={text.intro} />
        <ul className="grid gap-4 md:grid-cols-3">
          {text.tiles.map(({ key, label, text: body }, index) => (
            <GlassCard key={key} as="li" hover className="reveal flex flex-col gap-2" style={{ "--i": index + 1 }}>
              <p className="text-sm font-medium text-muted-foreground">{label}</p>
              <p className="text-4xl font-semibold tracking-tight sm:text-5xl">{values[key]}</p>
              <p className="text-sm text-muted-foreground">{body}</p>
            </GlassCard>
          ))}
        </ul>
        <p className="reveal flex items-center justify-center gap-2 text-sm font-medium" style={{ "--i": 4 }}>
          <FlaskConical className="size-4 text-muted-foreground" aria-hidden="true" />
          {text.note}
        </p>
      </div>
    </InView>
  );
}
