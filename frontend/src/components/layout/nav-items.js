import {
  ArrowLeftRight,
  BarChart3,
  Bell,
  LayoutDashboard,
  ScrollText,
  Settings,
  ShieldCheck,
  Smartphone,
  Upload,
} from "lucide-react";
import { canOpen } from "@/lib/permissions";
import { copy } from "@/lib/copy";

// Seven top-level items at most per role (Hick's law). `page` is the key in
// lib/permissions.js that decides who sees the item.
export const navItems = [
  { href: "/admin", page: "admin", label: copy.nav.admin, Icon: ShieldCheck },
  { href: "/dashboard", page: "dashboard", label: copy.nav.dashboard, Icon: LayoutDashboard },
  { href: "/sims", page: "sims", label: copy.nav.sims, Icon: Smartphone },
  { href: "/alerts", page: "alerts", label: copy.nav.alerts, Icon: Bell },
  { href: "/usage", page: "usage", label: copy.nav.usage, Icon: BarChart3 },
  { href: "/allocations", page: "allocations", label: copy.nav.allocations, Icon: ArrowLeftRight },
  { href: "/imports", page: "imports", label: copy.nav.imports, Icon: Upload },
  { href: "/audit-log", page: "auditLog", label: copy.nav.auditLog, Icon: ScrollText },
  { href: "/settings", page: "settings", label: copy.nav.settings, Icon: Settings },
];

/** The items this role may open. */
export function navItemsFor(role) {
  return navItems.filter((item) => canOpen(role, item.page));
}

export function isActive(pathname, href) {
  return pathname === href || pathname.startsWith(`${href}/`);
}
