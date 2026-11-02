import { GlassBackdrop } from "@/components/shared/glass-backdrop";
import { CtaBand } from "@/components/landing/cta-band";
import { Features } from "@/components/landing/features";
import { Hero } from "@/components/landing/hero";
import { HowItWorks } from "@/components/landing/how-it-works";
import { LandingFooter } from "@/components/landing/landing-footer";
import { LandingNav } from "@/components/landing/landing-nav";
import { LandingShell } from "@/components/landing/landing-shell";
import { ProductPreview } from "@/components/landing/product-preview";
import { StatsStrip } from "@/components/landing/stats-strip";
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
      <LandingNav />
      <main id="main" tabIndex={-1} className="flex-1">
        <Hero />
        <ProductPreview />
        <StatsStrip />
        <Features />
        <HowItWorks />
        <CtaBand />
      </main>
      <LandingFooter />
    </LandingShell>
  );
}
