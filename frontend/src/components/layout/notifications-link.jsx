"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { Bell } from "lucide-react";
import { useAuth } from "@/components/auth/auth-provider";
import { IconButton } from "@/components/layout/icon-button";
import { countOpenAlerts, countPendingAccessRequests } from "@/lib/data";
import { ROLES } from "@/lib/auth/core";
import { copy } from "@/lib/copy";

const text = copy.shell;
const REFRESH_MS = 60_000;

/** What the bell counts and where it goes, by role. */
function shortcutFor(role) {
  if (role === ROLES.admin) {
    return {
      href: "/admin?tab=requests",
      load: countPendingAccessRequests,
      label: (count) => (count === 0 ? text.noPendingRequests : text.pendingRequests(count)),
      unknown: text.pendingRequestsUnknown,
    };
  }
  return {
    href: "/alerts",
    load: countOpenAlerts,
    label: (count) => (count === 0 ? text.noOpenAlerts : text.openAlerts(count)),
    unknown: text.openAlertsUnknown,
  };
}

/**
 * The bell: a link with a real count. Security Officer and Auditor see the
 * open alerts and go to Alerts; the Admin sees the pending access requests
 * and goes to the requests tab. The red dot and the number show only above
 * zero. The count loads with the page, on each page change and every 60
 * seconds (not while the tab is hidden). If a load fails, the last count is
 * kept; before the first one the link has a plain name without a number.
 */
export function NotificationsLink() {
  const { role } = useAuth();
  const pathname = usePathname();
  const [count, setCount] = useState(null);
  const shortcut = shortcutFor(role);

  useEffect(() => {
    let cancelled = false;
    const { load } = shortcutFor(role);

    async function refresh() {
      if (document.hidden) return;
      try {
        const next = await load();
        if (!cancelled) setCount(next);
      } catch {
        // Keep the last count; the next refresh tries again.
      }
    }

    refresh();
    const timer = setInterval(refresh, REFRESH_MS);
    document.addEventListener("visibilitychange", refresh);

    return () => {
      cancelled = true;
      clearInterval(timer);
      document.removeEventListener("visibilitychange", refresh);
    };
  }, [role, pathname]);

  const label = count === null ? shortcut.unknown : shortcut.label(count);

  return (
    <IconButton asChild label={label} className="hidden w-auto min-w-10 gap-1 px-2.5 sm:inline-flex">
      <Link href={shortcut.href}>
        <span className="relative inline-flex">
          <Bell aria-hidden="true" />
          {count > 0 && <span className="notify-dot" aria-hidden="true" />}
        </span>
        {count > 0 && (
          <span className="text-xs font-medium tabular-nums" aria-hidden="true">
            {count > 99 ? "99+" : count}
          </span>
        )}
      </Link>
    </IconButton>
  );
}
