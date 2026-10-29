"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { PanelLeftClose, PanelLeftOpen } from "lucide-react";
import { Logo } from "@/components/layout/logo";
import { isActive, navItems } from "@/components/layout/nav-items";
import { Button } from "@/components/ui/button";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const STORAGE_KEY = "simguard.sidebar.collapsed";

/**
 * Navigation links, shared by the desktop sidebar and the mobile sheet.
 * Labels are always visible except in the collapsed sidebar, where each
 * icon keeps its label as the accessible name and a tooltip.
 * @param {{ collapsed?: boolean, onNavigate?: () => void }} props
 */
export function NavList({ collapsed = false, onNavigate }) {
  const pathname = usePathname();

  return (
    <ul className="flex flex-col gap-1">
      {navItems.map(({ href, label, Icon }) => {
        const active = isActive(pathname, href);

        const link = (
          <Link
            href={href}
            onClick={onNavigate}
            aria-current={active ? "page" : undefined}
            aria-label={collapsed ? label : undefined}
            className={cn(
              "glass-nav-item flex h-10 items-center gap-3 rounded-lg px-3 text-sm text-foreground transition-colors duration-150 hover:bg-accent",
              collapsed && "justify-center px-0"
            )}
          >
            <Icon className="size-4 shrink-0" aria-hidden="true" />
            {!collapsed && <span className="truncate">{label}</span>}
          </Link>
        );

        return (
          <li key={href}>
            {collapsed ? (
              <Tooltip>
                <TooltipTrigger asChild>{link}</TooltipTrigger>
                <TooltipContent side="right">{label}</TooltipContent>
              </Tooltip>
            ) : (
              link
            )}
          </li>
        );
      })}
    </ul>
  );
}

export function AppSidebar() {
  const [collapsed, setCollapsed] = useState(false);

  // Restore the saved preference after hydration so server and client match.
  useEffect(() => {
    try {
      if (window.localStorage.getItem(STORAGE_KEY) === "true") {
        // eslint-disable-next-line react-hooks/set-state-in-effect -- one-time sync from storage
        setCollapsed(true);
      }
    } catch {
      // Storage unavailable; keep the default.
    }
  }, []);

  function toggle() {
    setCollapsed((value) => {
      const next = !value;
      try {
        window.localStorage.setItem(STORAGE_KEY, String(next));
      } catch {
        // Storage unavailable; the choice lasts for this visit only.
      }
      return next;
    });
  }

  const toggleLabel = collapsed ? copy.sidebar.expand : copy.sidebar.collapse;

  return (
    <aside
      className={cn(
        "glass-chrome sticky top-0 hidden h-dvh shrink-0 flex-col border-r transition-[width] duration-200 ease-out md:flex",
        collapsed ? "w-16" : "w-[220px]"
      )}
    >
      <div className={cn("flex h-14 items-center px-4", collapsed && "justify-center px-0")}>
        <Link href="/dashboard" className="rounded-md">
          <Logo showName={!collapsed} />
        </Link>
      </div>

      <nav aria-label={copy.nav.label} className={cn("flex-1 px-3 py-2", collapsed && "px-2")}>
        <NavList collapsed={collapsed} />
      </nav>

      <div className={cn("p-3", collapsed && "flex justify-center px-2")}>
        <Button
          variant="ghost"
          size="icon"
          onClick={toggle}
          aria-label={toggleLabel}
          aria-expanded={!collapsed}
        >
          {collapsed ? (
            <PanelLeftOpen aria-hidden="true" />
          ) : (
            <PanelLeftClose aria-hidden="true" />
          )}
        </Button>
      </div>
    </aside>
  );
}
