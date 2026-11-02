import { Ghost, ReceiptText, Share2, Store } from "lucide-react";
import { GlassCard } from "@/components/glass/glass-card";
import { InView } from "@/components/landing/in-view";
import { SectionHeading } from "@/components/landing/section-heading";
import { copy } from "@/lib/copy";

const text = copy.landing.problem;
const icons = { ghostUsage: Ghost, sharing: Share2, reselling: Store, inactiveBilling: ReceiptText };

/** The four misuse types, each with one example signal. */
export function Problem() {
  return (
    <InView as="section" id="features" aria-labelledby="features-title" threshold={0.1} className="px-4 pb-24 sm:px-6">
      <div className="mx-auto flex max-w-5xl flex-col gap-10">
        <SectionHeading id="features-title" title={text.title} intro={text.intro} />
        <ul className="grid gap-4 sm:grid-cols-2">
          {text.items.map(({ key, title, text: body, signal }, index) => {
            const Icon = icons[key];

            return (
              <GlassCard key={key} as="li" hover className="reveal flex flex-col gap-3" style={{ "--i": index + 1 }}>
                <span className="flex size-10 items-center justify-center rounded-full bg-primary text-primary-foreground">
                  <Icon className="size-5" aria-hidden="true" />
                </span>
                <h3 className="text-xl font-semibold tracking-tight">{title}</h3>
                <p className="text-muted-foreground">{body}</p>
                <p className="mt-auto pt-1">
                  <span className="sr-only">{`${text.signalLabel}: `}</span>
                  <span className="inline-flex min-h-7 items-center gap-1.5 rounded-full border border-input bg-glass-strong px-3 py-1 text-xs font-medium">
                    <span className="size-1.5 shrink-0 rounded-full bg-risk-escalated-strong" aria-hidden="true" />
                    {signal}
                  </span>
                </p>
              </GlassCard>
            );
          })}
        </ul>
      </div>
    </InView>
  );
}
