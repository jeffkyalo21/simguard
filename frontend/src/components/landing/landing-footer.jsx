import Link from "next/link";
import { Logo } from "@/components/layout/logo";
import { ThemeToggle } from "@/components/layout/theme-toggle";
import { RegisterButton } from "@/components/landing/register-dialog";
import { FooterWordmark } from "@/components/landing/footer-wordmark";
import { InView } from "@/components/landing/in-view";
import { ScrollLink } from "@/components/landing/scroll-link";
import { copy } from "@/lib/copy";

const text = copy.landing;
const linkClass = "footer-link inline-flex min-h-8 items-center text-sm text-muted-foreground hover:text-foreground";

const groups = [
  {
    key: "product",
    title: text.footer.product,
    links: [
      { href: "#features", label: text.nav.features },
      { href: "#how-it-works", label: text.nav.how },
      { href: "#results", label: text.nav.results },
      { href: "#faq", label: text.nav.faq },
    ],
  },
  {
    key: "detects",
    title: text.footer.detects,
    links: text.problem.items.map(({ key, title }) => ({ key, href: "#features", label: title })),
  },
];

/**
 * A wide glass panel with link columns, set apart from the pill nav at the
 * top. Each link group is its own labelled nav.
 */
export function LandingFooter() {
  return (
    <footer aria-label={text.footer.label} className="px-4 pb-6 sm:px-6">
      <InView threshold={0.15} className="mx-auto max-w-[1280px]">
        <div className="footer-panel glass-card reveal relative overflow-hidden px-6 pt-10 pb-6 sm:px-10 sm:pt-12">
          <FooterWordmark />

          <div className="relative grid gap-x-8 gap-y-10 sm:grid-cols-2 lg:grid-cols-4">
            <div className="flex flex-col gap-3">
              <Logo />
              <p className="max-w-64 text-sm text-muted-foreground">{text.footer.tagline}</p>
            </div>

            {groups.map(({ key, title, links }) => (
              <nav key={key} aria-labelledby={`footer-${key}`} className="flex flex-col gap-2">
                <p id={`footer-${key}`} className="text-sm font-semibold">
                  {title}
                </p>
                <ul className="flex flex-col">
                  {links.map((link) => (
                    <li key={link.key ?? link.href}>
                      <ScrollLink href={link.href} className={linkClass}>
                        {link.label}
                      </ScrollLink>
                    </li>
                  ))}
                </ul>
              </nav>
            ))}

            <nav aria-labelledby="footer-touch" className="flex flex-col gap-2">
              <p id="footer-touch" className="text-sm font-semibold">
                {text.footer.touch}
              </p>
              <ul className="flex flex-col">
                <li>
                  <ScrollLink href="#contact" className={linkClass}>
                    {text.footer.contactUs}
                  </ScrollLink>
                </li>
                <li>
                  <RegisterButton className={linkClass}>{text.registerLabel}</RegisterButton>
                </li>
                <li>
                  <Link href="/login" className={linkClass}>
                    {copy.actions.signIn}
                  </Link>
                </li>
              </ul>
            </nav>
          </div>

          <div className="relative mt-10 h-px bg-glass-border" aria-hidden="true" />

          <div className="relative mt-4 flex items-center justify-between gap-4">
            <p className="text-sm text-muted-foreground">{text.footer.copyright}</p>
            <ThemeToggle />
          </div>
        </div>
      </InView>
    </footer>
  );
}
