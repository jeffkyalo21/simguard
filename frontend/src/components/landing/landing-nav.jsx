import Link from "next/link";
import { Logo } from "@/components/layout/logo";
import { ThemeToggle } from "@/components/layout/theme-toggle";
import { Button } from "@/components/ui/button";
import { copy } from "@/lib/copy";

const links = [
  { href: "#features", label: copy.landing.nav.features },
  { href: "#how", label: copy.landing.nav.how },
];

/** Sticky glass nav. LandingShell sets data-scrolled for the stronger look. */
export function LandingNav() {
  return (
    <header data-landing-nav className="landing-nav glass-chrome sticky top-0 z-40 border-b border-transparent">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-4 px-4 sm:px-6">
        <Link href="/" className="rounded-md">
          <Logo />
        </Link>
        <nav aria-label={copy.landing.nav.label} className="hidden sm:block">
          <ul className="flex items-center gap-1">
            {links.map(({ href, label }) => (
              <li key={href}>
                <a
                  href={href}
                  className="inline-flex h-10 items-center rounded-lg px-3 text-sm text-muted-foreground transition-colors duration-150 hover:bg-accent hover:text-foreground"
                >
                  {label}
                </a>
              </li>
            ))}
          </ul>
        </nav>
        <div className="flex items-center gap-2">
          <ThemeToggle />
          <Button asChild size="lg" className="h-10 px-4">
            <Link href="/login">{copy.actions.signIn}</Link>
          </Button>
        </div>
      </div>
    </header>
  );
}
