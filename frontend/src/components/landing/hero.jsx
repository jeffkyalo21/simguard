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
 * The headline is typed in by GSAP (landing-motion.jsx). Every character is
 * its own span, so the full text takes its space from the first frame and
 * nothing reflows while it types. The heading carries the whole text in
 * aria-label and the animated spans are hidden from assistive technology.
 * Without JavaScript or with reduced motion the text simply shows. The
 * accent words keep the brand gradient.
 */
function Headline() {
  const words = [
    ...text.heroLead.split(" ").map((word) => ({ word, accent: false })),
    ...text.heroAccent.split(" ").map((word) => ({ word, accent: true })),
  ];

  return (
    <h1
      id="hero-title"
      aria-label={`${text.heroLead} ${text.heroAccent}`}
      className={`${display.className} relative text-[clamp(3rem,9vw,6.5rem)] leading-[0.95] font-bold tracking-[-0.035em] text-balance`}
    >
      <span aria-hidden="true">
        {words.map(({ word, accent }, index) => (
          <span key={`${word}-${index}`}>
            {index > 0 && " "}
            <span className={accent ? "type-word hero-accent" : "type-word"}>
              {[...word].map((character, position) => (
                <span key={position} data-char className="type-char">
                  {character}
                </span>
              ))}
            </span>
          </span>
        ))}
        <span data-cursor className="type-cursor">
          <span data-cursor-bar className="type-bar" />
        </span>
      </span>
    </h1>
  );
}

export function Hero() {
  return (
    <section data-hero aria-labelledby="hero-title" className="relative isolate px-4 pt-20 pb-14 sm:px-6 sm:pt-28">
      <div className="pointer-events-none absolute inset-0 -z-10 overflow-hidden" aria-hidden="true">
        <div data-glow className="hero-glow absolute top-[8%] left-[12%] size-[26rem] rounded-full" />
        <div data-glow className="hero-glow absolute top-[22%] right-[8%] size-[22rem] rounded-full" />
      </div>
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
