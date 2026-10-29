import {
  ArrowLeftRight,
  BarChart3,
  Bell,
  LayoutDashboard,
  ScrollText,
  Settings,
  Smartphone,
} from "lucide-react";
import { copy } from "@/lib/copy";

// Seven top-level items at most (Hick's law).
export const navItems = [
  { href: "/dashboard", label: copy.nav.dashboard, Icon: LayoutDashboard },
  { href: "/sims", label: copy.nav.sims, Icon: Smartphone },
  { href: "/alerts", label: copy.nav.alerts, Icon: Bell },
  { href: "/usage", label: copy.nav.usage, Icon: BarChart3 },
  { href: "/allocations", label: copy.nav.allocations, Icon: ArrowLeftRight },
  { href: "/audit-log", label: copy.nav.auditLog, Icon: ScrollText },
  { href: "/settings", label: copy.nav.settings, Icon: Settings },
];

export function isActive(pathname, href) {
  return pathname === href || pathname.startsWith(`${href}/`);
}
