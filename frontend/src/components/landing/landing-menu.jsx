"use client";

import { useRef, useState } from "react";
import Link from "next/link";
import { Menu } from "lucide-react";
import { Logo } from "@/components/layout/logo";
import { useOpenDemo } from "@/components/landing/demo-dialog";
import { scrollToSection } from "@/components/landing/scroll-link";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { copy } from "@/lib/copy";

const text = copy.landing;

/**
 * Below 1100px the section links and both calls to action move into a
 * sheet. A choice closes the sheet first; once it has closed, the page
 * scrolls to the section or the demo dialog opens (focus then returns to
 * the Menu button).
 * @param {{ links: { href: string, label: string }[] }} props
 */
export function LandingMenu({ links }) {
  const [open, setOpen] = useState(false);
  const pending = useRef(null);
  const triggerRef = useRef(null);
  const openDemo = useOpenDemo();

  function choose(event, next) {
    if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    pending.current = next;
    setOpen(false);
  }

  function onCloseAutoFocus(event) {
    const next = pending.current;
    pending.current = null;
    if (!next) return;
    event.preventDefault();
    if (next === "demo") openDemo(triggerRef.current);
    else scrollToSection(next);
  }

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger asChild>
        <Button ref={triggerRef} variant="ghost" className="h-10 gap-2 px-4 min-[1100px]:hidden">
          <Menu aria-hidden="true" />
          {copy.shell.menu}
        </Button>
      </SheetTrigger>
      <SheetContent
        side="right"
        onCloseAutoFocus={onCloseAutoFocus}
        className="w-[300px] gap-2 rounded-l-[var(--radius-card)] p-0"
      >
        <SheetHeader className="h-16 justify-center px-5">
          <SheetTitle asChild>
            <div>
              <Logo />
            </div>
          </SheetTitle>
        </SheetHeader>
        <nav aria-label={text.nav.label} className="px-3">
          <ul className="flex flex-col gap-1">
            {links.map(({ href, label }) => (
              <li key={href}>
                <a
                  href={href}
                  onClick={(event) => choose(event, href)}
                  className="flex h-11 items-center rounded-full px-4 text-sm font-medium transition-colors duration-150 hover:bg-accent"
                >
                  {label}
                </a>
              </li>
            ))}
          </ul>
        </nav>
        <div className="mt-auto flex flex-col gap-2 p-5">
          <Button asChild size="lg" className="h-11">
            <a href="#contact" aria-haspopup="dialog" onClick={(event) => choose(event, "demo")}>
              {text.bookDemo}
            </a>
          </Button>
          <Button asChild size="lg" variant="outline" className="h-11">
            <Link href="/login" onClick={() => setOpen(false)}>
              {copy.actions.signIn}
            </Link>
          </Button>
        </div>
      </SheetContent>
    </Sheet>
  );
}
