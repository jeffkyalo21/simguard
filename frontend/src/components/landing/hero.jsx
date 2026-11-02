import Link from "next/link";
import { ArrowRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { copy } from "@/lib/copy";

const text = copy.landing;

/**
 * Headline words animate in one by one with CSS only, so the text is in
 * the server HTML and visible without JavaScript. The accent words carry
 * the brand colour.
 */
function Headline() {
  const words = [
    ...text.heroLead.split(" ").map((word) => ({ word, accent: false })),
    ...text.heroAccent.split(" ").map((word) => ({ word, accent: true })),
  ];

  return (
    <h1 id="hero-title" className="text-5xl font-semibold tracking-tight text-balance sm:text-7xl">
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
    <section aria-labelledby="hero-title" className="relative isolate px-4 pt-20 pb-12 sm:px-6 sm:pt-28">
      <div className="landing-dots absolute inset-0 -z-10" aria-hidden="true" />
      <div className="landing-scrim absolute inset-0 -z-10" aria-hidden="true" />
      <div className="mx-auto flex max-w-3xl flex-col items-center gap-6 text-center">
        <Headline />
        <p className="hero-fade text-lg text-muted-foreground sm:text-xl" style={{ "--i": 0 }}>
          {text.heroSubtitle}
        </p>
        <div className="hero-fade" style={{ "--i": 1 }}>
          <Button asChild size="lg" className="h-11 px-6 text-base">
            <Link href="/login">
              {copy.actions.signIn}
              <ArrowRight aria-hidden="true" />
            </Link>
          </Button>
        </div>
      </div>
    </section>
  );
}
