"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Lock } from "lucide-react";
import { useAuth } from "@/components/auth/auth-provider";
import { Button } from "@/components/ui/button";
import { homeFor } from "@/lib/auth/core";
import { canOpenPath } from "@/lib/permissions";
import { copy } from "@/lib/copy";

/** Shown instead of a page the role may not open. */
export function AccessDenied({ role }) {
  const text = copy.permissions;

  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-4 px-4 py-16 text-center">
      <Lock className="size-10 text-muted-foreground" aria-hidden="true" />
      <h1 className="text-2xl font-semibold tracking-tight">{text.deniedTitle}</h1>
      <p className="text-muted-foreground">{text.deniedText(copy.roles[role] ?? role)}</p>
      <Button asChild className="h-10 px-4">
        <Link href={homeFor(role)}>{text.goToHome}</Link>
      </Button>
    </div>
  );
}

/**
 * Renders the page only when the signed in role may open it (see
 * lib/permissions.js). Runs during server rendering too, so a forbidden
 * URL never flashes the page. The backend still enforces every rule.
 */
export function RouteGuard({ children }) {
  const pathname = usePathname();
  const { role } = useAuth();

  if (!canOpenPath(role, pathname)) return <AccessDenied role={role} />;
  return children;
}
