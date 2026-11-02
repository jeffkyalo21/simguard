import { ChevronDown } from "lucide-react";
import { GlassCard } from "@/components/glass/glass-card";
import { InView } from "@/components/landing/in-view";
import { SectionHeading } from "@/components/landing/section-heading";
import { copy } from "@/lib/copy";

const text = copy.landing.faq;

/**
 * Native details and summary: keyboard and screen reader support comes
 * from the browser, and every answer is readable without JavaScript.
 */
export function Faq() {
  return (
    <InView as="section" id="faq" aria-labelledby="faq-title" threshold={0.1} className="px-4 pb-24 sm:px-6">
      <div className="mx-auto flex max-w-3xl flex-col gap-10">
        <SectionHeading id="faq-title" title={text.title} intro={text.intro} />
        <GlassCard variant="strong" className="reveal p-2 sm:p-3" style={{ "--i": 1 }}>
          {text.items.map(({ key, question, answer }) => (
            <details key={key} className="faq-item group border-b border-border last:border-0">
              <summary className="flex min-h-14 cursor-pointer list-none items-center justify-between gap-4 rounded-2xl px-4 py-3 font-medium hover:bg-accent [&::-webkit-details-marker]:hidden">
                <span>{question}</span>
                <ChevronDown
                  className="faq-chevron size-4 shrink-0 text-muted-foreground group-open:rotate-180"
                  aria-hidden="true"
                />
              </summary>
              <p className="faq-answer px-4 pt-1 pb-4 text-muted-foreground">{answer}</p>
            </details>
          ))}
        </GlassCard>
      </div>
    </InView>
  );
}
