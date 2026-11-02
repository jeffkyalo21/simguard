import { Check, Landmark, ShieldCheck, Smartphone } from "lucide-react";
import { GlassCard } from "@/components/glass/glass-card";
import { InView } from "@/components/landing/in-view";
import { SectionHeading } from "@/components/landing/section-heading";
import { copy } from "@/lib/copy";

const text = copy.landing.audience;
const icons = { fleet: Smartphone, finance: Landmark, security: ShieldCheck };

/** Three roles, two short points each. */
export function Audience() {
  return (
    <InView as="section" aria-labelledby="audience-title" threshold={0.1} className="px-4 pb-24 sm:px-6">
      <div className="mx-auto flex max-w-5xl flex-col gap-10">
        <SectionHeading id="audience-title" title={text.title} intro={text.intro} />
        <ul className="grid gap-4 md:grid-cols-3">
          {text.items.map(({ key, title, points }, index) => {
            const Icon = icons[key];

            return (
              <GlassCard key={key} as="li" hover className="reveal flex flex-col gap-4" style={{ "--i": index + 1 }}>
                <span className="flex items-center gap-3">
                  <span className="flex size-10 items-center justify-center rounded-full bg-primary text-primary-foreground">
                    <Icon className="size-5" aria-hidden="true" />
                  </span>
                  <h3 className="text-xl font-semibold tracking-tight">{title}</h3>
                </span>
                <ul className="flex flex-col gap-2.5">
                  {points.map((point) => (
                    <li key={point} className="flex gap-2.5 text-muted-foreground">
                      <Check className="mt-1 size-4 shrink-0 text-foreground" aria-hidden="true" />
                      {point}
                    </li>
                  ))}
                </ul>
              </GlassCard>
            );
          })}
        </ul>
      </div>
    </InView>
  );
}
