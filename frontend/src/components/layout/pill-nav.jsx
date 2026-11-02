"use client";

import { useEffect, useLayoutEffect, useRef, useState } from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { Bell, LogOut, Menu, Search } from "lucide-react";
import { IconButton } from "@/components/layout/icon-button";
import { Logo } from "@/components/layout/logo";
import { isActive, navItems } from "@/components/layout/nav-items";
import { ThemeToggle } from "@/components/layout/theme-toggle";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const text = copy.shell;

function isTypingTarget(target) {
  return (
    target instanceof HTMLElement &&
    (target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName))
  );
}

// A page's own search (data-page-search) wins over the nav search. Pages
// kept hidden by navigation stay in the DOM, so only visible ones count.
function visiblePageSearch() {
  return [...document.querySelectorAll("[data-page-search]")].find(
    (element) => element.offsetParent !== null
  );
}

/**
 * The seven links in a glass pill. A shared ink indicator slides to the
 * active link (FLIP: the new size is set at once, then a transform eases
 * from the old position, so only transform animates). Until it is
 * measured, and without JavaScript, the active link draws its own pill.
 */
function CentreNav() {
  const pathname = usePathname();
  const navRef = useRef(null);
  const indicatorRef = useRef(null);
  const previous = useRef(null);

  useLayoutEffect(() => {
    const nav = navRef.current;
    const indicator = indicatorRef.current;

    function place(animate) {
      const active = nav.querySelector('[aria-current="page"]');
      if (!active) {
        indicator.style.visibility = "hidden";
        previous.current = null;
        return;
      }

      const next = { x: active.offsetLeft, y: active.offsetTop, w: active.offsetWidth, h: active.offsetHeight };
      const from = previous.current;
      indicator.style.visibility = "visible";
      indicator.style.width = `${next.w}px`;
      indicator.style.height = `${next.h}px`;
      previous.current = next;

      if (!animate || !from || from.x === next.x) {
        delete indicator.dataset.animate;
        indicator.style.transform = `translate(${next.x}px, ${next.y}px)`;
        return;
      }

      // Start where the old link was, scaled to its width, then ease home.
      delete indicator.dataset.animate;
      indicator.style.transform = `translate(${from.x}px, ${next.y}px) scaleX(${(from.w / next.w).toFixed(4)})`;
      void indicator.offsetWidth;
      indicator.dataset.animate = "";
      indicator.style.transform = `translate(${next.x}px, ${next.y}px)`;
    }

    place(true);
    nav.dataset.ready = "";

    // Fonts and resizes change link widths; re-place without animating.
    const observer = new ResizeObserver(() => place(false));
    observer.observe(nav);
    return () => observer.disconnect();
  }, [pathname]);

  return (
    <nav ref={navRef} aria-label={copy.nav.label} className="pill-nav glass-pill relative hidden p-1 min-[1100px]:block">
      <span ref={indicatorRef} className="pill-indicator" aria-hidden="true" />
      {/* No "relative" here: link offsets must be measured from the nav, the
          same origin the indicator is placed from. */}
      <ul className="flex items-center gap-0.5">
        {navItems.map(({ href, label }) => (
          <li key={href}>
            <Link
              href={href}
              aria-current={isActive(pathname, href) ? "page" : undefined}
              className="pill-link inline-flex h-9 items-center rounded-full px-4 text-sm font-medium whitespace-nowrap"
            >
              {label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}

/** Below 1100px the links move into a sheet behind a "Menu" button. */
function MenuSheet() {
  const pathname = usePathname();
  const [open, setOpen] = useState(false);

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger asChild>
        <Button variant="ghost" className="glass-pill touch-target h-11 gap-2 px-4 min-[1100px]:hidden">
          <Menu aria-hidden="true" />
          {text.menu}
        </Button>
      </SheetTrigger>
      <SheetContent side="left" className="w-[280px] gap-2 rounded-r-[var(--radius-card)] p-0">
        <SheetHeader className="h-16 justify-center px-5">
          <SheetTitle asChild>
            <div>
              <Logo />
            </div>
          </SheetTitle>
        </SheetHeader>
        <nav aria-label={copy.nav.label} className="px-3">
          <ul className="flex flex-col gap-1">
            {navItems.map(({ href, label, Icon }) => {
              const active = isActive(pathname, href);
              return (
                <li key={href}>
                  <Link
                    href={href}
                    onClick={() => setOpen(false)}
                    aria-current={active ? "page" : undefined}
                    className={cn(
                      "flex h-11 items-center gap-3 rounded-full px-4 text-sm font-medium transition-colors duration-150",
                      active ? "bg-primary text-primary-foreground" : "hover:bg-accent"
                    )}
                  >
                    <Icon className="size-4 shrink-0" aria-hidden="true" />
                    {label}
                  </Link>
                </li>
              );
            })}
          </ul>
        </nav>
      </SheetContent>
    </Sheet>
  );
}

/**
 * Search: an icon button that expands into a field on click or "/".
 * Escape (or leaving an empty field) closes it. Submitting opens the SIM
 * list filtered by the query. On pages with their own search, "/" focuses
 * that instead.
 */
function SearchControl() {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const inputRef = useRef(null);
  const buttonRef = useRef(null);

  useEffect(() => {
    function onKeyDown(event) {
      if (event.key !== "/" || event.metaKey || event.ctrlKey || event.altKey || isTypingTarget(event.target)) return;
      event.preventDefault();
      const pageSearch = visiblePageSearch();
      if (pageSearch) pageSearch.focus();
      else setOpen(true);
    }

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, []);

  useEffect(() => {
    if (open) inputRef.current?.focus();
  }, [open]);

  function close({ restoreFocus = true } = {}) {
    setOpen(false);
    if (restoreFocus) requestAnimationFrame(() => buttonRef.current?.focus());
  }

  function handleSubmit(event) {
    event.preventDefault();
    const query = inputRef.current?.value.trim();
    close({ restoreFocus: false });
    router.push(query ? `/sims?q=${encodeURIComponent(query)}` : "/sims");
  }

  return (
    <>
      <IconButton
        ref={buttonRef}
        label={text.search}
        aria-expanded={open}
        aria-keyshortcuts={text.searchShortcut}
        onClick={() => setOpen(true)}
        className={cn(open && "invisible")}
      >
        <Search aria-hidden="true" />
      </IconButton>
      {open && (
        <form
          role="search"
          onSubmit={handleSubmit}
          className="search-pop absolute inset-y-0 right-0 z-10 flex w-[min(240px,calc(100vw-6rem))] items-center rounded-full border border-glass-border bg-popover shadow-[var(--glass-shadow)]"
        >
          <label className="relative flex h-full w-full items-center">
            <span className="sr-only">{text.search}</span>
            <Search className="pointer-events-none absolute left-3.5 size-4 text-muted-foreground" aria-hidden="true" />
            <input
              ref={inputRef}
              type="search"
              name="q"
              placeholder={text.searchPlaceholder}
              onKeyDown={(event) => {
                if (event.key === "Escape") {
                  event.preventDefault();
                  close();
                }
              }}
              onBlur={(event) => {
                if (!event.currentTarget.value) close({ restoreFocus: false });
              }}
              className="h-full w-full min-w-0 rounded-full bg-transparent pr-4 pl-10 text-sm text-foreground outline-none placeholder:text-muted-foreground"
            />
          </label>
        </form>
      )}
    </>
  );
}

function UserMenu() {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <IconButton label={`${text.account}: ${copy.user.name}`}>
          <Avatar className="size-8">
            <AvatarFallback className="bg-primary text-xs font-semibold text-primary-foreground">
              {copy.user.initials}
            </AvatarFallback>
          </Avatar>
        </IconButton>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-44">
        <DropdownMenuLabel>{copy.user.name}</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link href="/login">
            <LogOut aria-hidden="true" />
            {copy.actions.signOut}
          </Link>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

/**
 * The app's floating navigation: round logo button, the centre pill of
 * links (a Menu sheet below 1100px) and a cluster of round controls.
 */
export function PillNav() {
  return (
    <header className="sticky top-3 z-40 px-4 sm:px-6">
      <div className="mx-auto flex max-w-[1280px] items-center justify-between gap-2">
        <div className="flex items-center gap-2 min-[1100px]:w-[200px]">
          <Link
            href="/dashboard"
            aria-label={copy.app.name}
            className="glass-pill touch-target flex size-11 shrink-0 items-center justify-center"
          >
            <Logo showName={false} className="[&>span:first-child]:size-7" />
          </Link>
          <MenuSheet />
        </div>

        <CentreNav />

        <div className="flex justify-end min-[1100px]:w-[200px]">
          <div className="glass-pill relative flex items-center gap-0.5 p-0.5">
            <SearchControl />
            <IconButton label={text.notifications} className="hidden sm:inline-flex">
              <Bell aria-hidden="true" />
              <span className="notify-dot" aria-hidden="true" />
            </IconButton>
            <ThemeToggle />
            <UserMenu />
          </div>
        </div>
      </div>
    </header>
  );
}
