import Link from "next/link";
import { ArrowLeftRight, ScanSearch, ShieldAlert } from "lucide-react";
import { Logo } from "@/components/layout/logo";
import { ThemeToggle } from "@/components/layout/theme-toggle";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { copy } from "@/lib/copy";

// Plain style: base shadcn tokens only, no glass.
const featureIcons = {
  detect: ShieldAlert,
  allocate: ArrowLeftRight,
  explain: ScanSearch,
};

export default function LandingPage() {
  return (
    <div className="flex min-h-dvh flex-col bg-background">
      <header className="border-b">
        <div className="mx-auto flex h-16 max-w-6xl items-center justify-between px-4 sm:px-6">
          <Logo />
          <div className="flex items-center gap-2">
            <ThemeToggle />
            <Button asChild className="h-10 px-4">
              <Link href="/login">{copy.actions.signIn}</Link>
            </Button>
          </div>
        </div>
      </header>

      <main id="main" tabIndex={-1} className="flex-1">
        <section className="mx-auto flex max-w-6xl flex-col items-center gap-6 px-4 pt-20 pb-12 text-center sm:px-6">
          <h1 className="text-4xl font-semibold tracking-tight sm:text-6xl">
            {copy.landing.heroTitle}
          </h1>
          <p className="text-lg text-muted-foreground">{copy.landing.heroSubtitle}</p>
          <Button asChild size="lg" className="h-11 px-6">
            <Link href="/login">{copy.actions.signIn}</Link>
          </Button>
        </section>

        <section className="mx-auto max-w-6xl px-4 sm:px-6">
          <div
            role="img"
            aria-label={copy.landing.previewLabel}
            className="mx-auto flex h-[340px] w-full max-w-[1000px] items-center justify-center rounded-xl border border-dashed bg-muted text-sm text-muted-foreground"
          >
            <span aria-hidden="true">{copy.landing.previewLabel}</span>
          </div>
        </section>

        <section className="mx-auto grid max-w-6xl gap-6 px-4 py-16 sm:grid-cols-3 sm:px-6">
          {copy.landing.features.map(({ key, title, text }) => {
            const Icon = featureIcons[key];

            return (
              <Card key={key}>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2">
                    <Icon className="size-4" aria-hidden="true" />
                    <h2>{title}</h2>
                  </CardTitle>
                </CardHeader>
                <CardContent className="text-muted-foreground">{text}</CardContent>
              </Card>
            );
          })}
        </section>
      </main>

      <footer className="border-t">
        <div className="mx-auto max-w-6xl px-4 py-6 text-sm text-muted-foreground sm:px-6">
          {copy.landing.footer}
        </div>
      </footer>
    </div>
  );
}
