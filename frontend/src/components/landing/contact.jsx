import { GlassCard } from "@/components/glass/glass-card";
import { InView } from "@/components/landing/in-view";
import { LeadForm } from "@/components/landing/lead-form";
import { SectionHeading } from "@/components/landing/section-heading";
import { copy } from "@/lib/copy";

const text = copy.landing.contact;

/** Contact form: general questions. Requests for an account use Register. */
export function Contact() {
  return (
    <InView as="section" id="contact" aria-labelledby="contact-title" threshold={0.1} className="px-4 pb-24 sm:px-6">
      <div className="mx-auto flex max-w-3xl flex-col gap-10">
        <SectionHeading id="contact-title" title={text.title} intro={text.intro} />
        <GlassCard variant="strong" className="reveal p-6 sm:p-8" style={{ "--i": 1 }}>
          <LeadForm kind="contact" submitLabel={text.submit} wide />
        </GlassCard>
      </div>
    </InView>
  );
}
