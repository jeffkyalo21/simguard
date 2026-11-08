import { GlassBackdrop } from "@/components/shared/glass-backdrop";
import { Audience } from "@/components/landing/audience";
import { CtaBand } from "@/components/landing/cta-band";
import { RegisterDialogProvider } from "@/components/landing/register-dialog";
import { Faq } from "@/components/landing/faq";
import { Hero } from "@/components/landing/hero";
import { HowItWorks } from "@/components/landing/how-it-works";
import { LandingFooter } from "@/components/landing/landing-footer";
import { LandingNav } from "@/components/landing/landing-nav";
import { LandingShell } from "@/components/landing/landing-shell";
import { Problem } from "@/components/landing/problem";
import { ProductPreview } from "@/components/landing/product-preview";
import { Results } from "@/components/landing/results";
import { Tour } from "@/components/landing/tour";
import { Trust } from "@/components/landing/trust";
import { WhyItFired } from "@/components/landing/why-it-fired";
import "./landing.css";

/**
 * The Landing page. Everything is server-rendered and readable without
 * JavaScript; small client islands add the motion. This folder is imported
 * only by the "/" route, so none of it reaches other routes' bundles.
 */
export function LandingPage() {
  return (
    <LandingShell>
      {/* The only animated backdrop in the product; it covers the static one. */}
      <GlassBackdrop animated />
      <RegisterDialogProvider>
        <LandingNav />
        <main id="main" tabIndex={-1} className="flex-1">
          <Hero />
          <ProductPreview />
          <Problem />
          <HowItWorks />
          <WhyItFired />
          <Results />
          <Tour />
          <Audience />
          <Trust />
          <Faq />
          <CtaBand />
        </main>
        <LandingFooter />
      </RegisterDialogProvider>
    </LandingShell>
  );
}
