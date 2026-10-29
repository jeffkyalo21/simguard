"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { Bell, LogOut, Menu, Search, Settings } from "lucide-react";
import { NavList } from "@/components/layout/app-sidebar";
import { Logo } from "@/components/layout/logo";
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
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet";
import { copy } from "@/lib/copy";

function isTypingTarget(target) {
  return (
    target instanceof HTMLElement &&
    (target.isContentEditable ||
      ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName))
  );
}

function MobileNav() {
  const [open, setOpen] = useState(false);

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger asChild>
        <Button variant="ghost" size="icon" className="md:hidden" aria-label={copy.topbar.openMenu}>
          <Menu aria-hidden="true" />
        </Button>
      </SheetTrigger>
      <SheetContent side="left" className="glass-app glass-popover w-[260px] gap-2 border-0 p-0">
        <SheetHeader className="h-14 justify-center px-4">
          <SheetTitle asChild>
            <div>
              <Logo />
            </div>
          </SheetTitle>
        </SheetHeader>
        <nav aria-label={copy.nav.label} className="px-3">
          <NavList onNavigate={() => setOpen(false)} />
        </nav>
      </SheetContent>
    </Sheet>
  );
}

function SearchField() {
  const inputRef = useRef(null);

  // "/" focuses search from anywhere except while typing in a field.
  useEffect(() => {
    function onKeyDown(event) {
      if (
        event.key === "/" &&
        !event.metaKey &&
        !event.ctrlKey &&
        !event.altKey &&
        !isTypingTarget(event.target)
      ) {
        event.preventDefault();
        inputRef.current?.focus();
      }
    }

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, []);

  return (
    <form role="search" onSubmit={(event) => event.preventDefault()} className="min-w-0 flex-1 sm:flex-none">
      <label className="relative flex h-9 w-full items-center rounded-lg bg-muted transition-shadow duration-150 focus-within:ring-2 focus-within:ring-ring sm:w-[360px]">
        <span className="sr-only">{copy.topbar.searchLabel}</span>
        <Search className="pointer-events-none absolute left-3 size-4 text-muted-foreground" aria-hidden="true" />
        <input
          ref={inputRef}
          type="search"
          placeholder={copy.topbar.searchPlaceholder}
          aria-keyshortcuts={copy.topbar.searchShortcut}
          className="h-full w-full min-w-0 rounded-lg bg-transparent pr-10 pl-9 text-sm text-foreground outline-none placeholder:text-muted-foreground focus-visible:outline-none"
        />
        <kbd
          aria-hidden="true"
          className="pointer-events-none absolute right-2 rounded border border-border px-1.5 font-mono text-xs text-muted-foreground"
        >
          {copy.topbar.searchShortcut}
        </kbd>
      </label>
    </form>
  );
}

function UserMenu() {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" className="h-10 gap-2 rounded-full px-1.5 sm:pr-3" aria-label={`${copy.topbar.userMenu}: ${copy.user.name}`}>
          <Avatar className="size-7">
            <AvatarFallback className="text-xs">{copy.user.initials}</AvatarFallback>
          </Avatar>
          <span className="hidden text-sm font-medium sm:inline">{copy.user.name}</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="glass-app glass-popover w-44">
        <DropdownMenuLabel>{copy.user.name}</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link href="/settings">
            <Settings aria-hidden="true" />
            {copy.nav.settings}
          </Link>
        </DropdownMenuItem>
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

export function Topbar() {
  return (
    <header className="glass-chrome sticky top-0 z-30 flex h-14 shrink-0 items-center justify-between gap-3 border-b px-4 sm:px-6">
      <div className="flex min-w-0 flex-1 items-center gap-2">
        <MobileNav />
        <SearchField />
      </div>

      <div className="flex shrink-0 items-center gap-1 sm:gap-2">
        <Button variant="ghost" size="icon" className="rounded-full" aria-label={copy.topbar.notifications}>
          <Bell aria-hidden="true" />
        </Button>
        <ThemeToggle glass />
        <UserMenu />
      </div>
    </header>
  );
}
