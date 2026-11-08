import Link from "next/link";
import { Logo } from "@/components/layout/logo";
import { ThemeToggle } from "@/components/layout/theme-toggle";
import { RegisterButton } from "@/components/landing/register-dialog";
import { LandingMenu } from "@/components/landing/landing-menu";
import { ScrollLink } from "@/components/landing/scroll-link";
import { Button } from "@/components/ui/button";
import { copy } from "@/lib/copy";

const text = copy.landing.nav;

const sectionLinks = [
  { href: "#features", label: text.features },
  { href: "#how-it-works", label: text.how },
  { href: "#results", label: text.results },
  { href: "#faq", label: text.faq },
  { href: "#contact", label: text.contact },
];

/**
 * Floating glass pill, the same family as the app nav. LandingShell sets
 * data-scrolled for the stronger blur and border once the page moves.
 * From 1100px the section links, Sign in and Register sit in the pill;
 * below that they move into the Menu sheet.
 */
export function LandingNav() {
  return (
    <header data-landing-nav className="landing-nav sticky top-3 z-40 px-4 sm:px-6">
      <div className="landing-nav-pill glass-pill mx-auto flex h-14 max-w-5xl items-center justify-between gap-3 pr-2 pl-5">
        <Link href="/" className="rounded-full">
          <Logo />
        </Link>
        <nav aria-label={text.label} className="hidden min-[1100px]:block">
          <ul className="flex items-center gap-0.5">
            {sectionLinks.map(({ href, label }) => (
              <li key={href}>
                <ScrollLink
                  href={href}
                  className="inline-flex h-10 items-center rounded-full px-3.5 text-sm font-medium whitespace-nowrap text-muted-foreground transition-colors duration-150 hover:bg-accent hover:text-foreground"
                >
                  {label}
                </ScrollLink>
              </li>
            ))}
          </ul>
        </nav>
        <div className="flex items-center gap-1">
          <ThemeToggle />
          <Button asChild variant="ghost" className="hidden h-10 px-4 min-[1100px]:inline-flex">
            <Link href="/login">{copy.actions.signIn}</Link>
          </Button>
          <Button asChild className="hidden h-10 px-5 min-[1100px]:inline-flex">
            <RegisterButton>{copy.landing.registerLabel}</RegisterButton>
          </Button>
          <LandingMenu links={sectionLinks} />
        </div>
      </div>
    </header>
  );
}
