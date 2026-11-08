import { Bricolage_Grotesque } from "next/font/google";
import { ArrowRight } from "lucide-react";
import { RegisterButton } from "@/components/landing/register-dialog";
import { ScrollLink } from "@/components/landing/scroll-link";
import { Button } from "@/components/ui/button";
import { copy } from "@/lib/copy";

// Display face for the hero headline only (self-hosted by next/font).
const display = Bricolage_Grotesque({ subsets: ["latin"], weight: "variable", display: "swap" });

const text = copy.landing;

/**
 * Headline words animate in one by one with CSS only, so the text is in
 * the server HTML and visible without JavaScript. The accent words carry
 * the brand gradient.
 */
function Headline() {
  const words = [
    ...text.heroLead.split(" ").map((word) => ({ word, accent: false })),
    ...text.heroAccent.split(" ").map((word) => ({ word, accent: true })),
  ];

  return (
    <h1
      id="hero-title"
      className={`${display.className} text-[clamp(3rem,9vw,6.5rem)] leading-[0.95] font-bold tracking-[-0.035em] text-balance`}
    >
      <span className="sr-only">{`${text.heroLead} ${text.heroAccent}`}</span>
      <span aria-hidden="true">
        {words.map(({ word, accent }, index) => (
          <span key={word}>
            {index > 0 && " "}
            <span className={accent ? "hero-word hero-accent" : "hero-word"} style={{ "--i": index }}>
              {word}
            </span>
          </span>
        ))}
      </span>
    </h1>
  );
}

export function Hero() {
  return (
    <section aria-labelledby="hero-title" className="relative isolate px-4 pt-20 pb-14 sm:px-6 sm:pt-28">
      <div className="landing-dots absolute inset-0 -z-10" aria-hidden="true" />
      <div className="landing-scrim absolute inset-0 -z-10" aria-hidden="true" />
      <div className="mx-auto flex max-w-4xl flex-col items-center gap-7 text-center">
        <Headline />
        <p className="hero-fade text-lg text-muted-foreground sm:text-xl" style={{ "--i": 0 }}>
          {text.heroSubtitle}
        </p>
        <div className="hero-fade flex flex-wrap items-center justify-center gap-3" style={{ "--i": 1 }}>
          <Button asChild size="lg" className="h-12 px-7 text-base">
            <RegisterButton>
              {text.registerLabel}
              <ArrowRight aria-hidden="true" />
            </RegisterButton>
          </Button>
          <Button asChild size="lg" variant="outline" className="h-12 px-7 text-base">
            <ScrollLink href="#how-it-works">{text.seeHow}</ScrollLink>
          </Button>
        </div>
      </div>
    </section>
  );
}
