import { ArrowRight } from "lucide-react";
import { BookDemoButton } from "@/components/landing/demo-dialog";
import { InView } from "@/components/landing/in-view";
import { Button } from "@/components/ui/button";
import { copy } from "@/lib/copy";

/** Closing call to action: moving gradient border and a soft glow ring. */
export function CtaBand() {
  return (
    <InView as="section" track threshold={0} aria-labelledby="cta-title" className="px-4 pb-24 sm:px-6">
      <div className="cta-border relative mx-auto max-w-5xl overflow-hidden rounded-[29px] p-px">
        <div className="cta-panel relative flex flex-col items-center gap-6 rounded-[28px] px-6 py-14 text-center">
          <div className="flex flex-col gap-3">
            <h2 id="cta-title" className="text-3xl font-semibold tracking-tight text-balance sm:text-4xl">
              {copy.landing.ctaTitle}
            </h2>
            <p className="text-lg text-muted-foreground">{copy.landing.ctaText}</p>
          </div>
          <span className="cta-glow relative rounded-full">
            <Button asChild size="lg" className="h-11 px-6 text-base">
              <BookDemoButton>
                {copy.landing.bookDemo}
                <ArrowRight aria-hidden="true" />
              </BookDemoButton>
            </Button>
          </span>
        </div>
      </div>
    </InView>
  );
}
